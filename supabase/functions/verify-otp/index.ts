// verify-otp
//
// Called by the ESP32 after the staff member enters the OTP on the keypad.
// Validates the code, then performs the server-side status-integrity check
// (TIME-IN vs TIME-OUT) and logs the attendance event. Also does late
// detection against shift_settings, and flags a possible buddy-punching
// attempt after repeated invalid OTP verifies.
//
// Request:  POST { rfid_uid: string, otp: string, action?: "TIME-IN" | "TIME-OUT" }
// Headers:  x-device-key: <DEVICE_API_KEY>
// Response: { status: "success" | "invalid" | "expired" | "unregistered"
//                    | "wrong_action" | "error", ... }
//
// `action` is the button the staff member pressed. It never decides the event
// type — the server derives that from the last attendance row — but when it
// disagrees, the request is rejected with { status: "wrong_action", expected }
// and the OTP is left unused so they can press the other button.

import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";
import { corsHeaders } from "../_shared/cors.ts";
import { formatFullName } from "../_shared/staff.ts";

const MAX_FAILED_ATTEMPTS = 3; // failed verifies within the window below
const FAILED_WINDOW_MINUTES = 10;
const SCHOOL_TIMEZONE = "Asia/Manila";

function minutesSinceMidnightIn(timeZone: string): number {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone,
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).formatToParts(new Date());
  const get = (type: string) =>
    Number(parts.find((p) => p.type === type)?.value ?? 0);
  return get("hour") * 60 + get("minute");
}

// Saturday and Sunday are rest days. Monday–Friday are the only regular
// workdays, so late and absent are never flagged on a weekend.
function isRestDayIn(timeZone: string): boolean {
  const weekday = new Intl.DateTimeFormat("en-US", {
    timeZone,
    weekday: "short",
  }).format(new Date());
  return weekday === "Sat" || weekday === "Sun";
}

function toMinutes(hhmmss: string): number {
  const [h, m] = hhmmss.split(":").map(Number);
  return h * 60 + m;
}

type Shift = {
  shift_start: string;
  late_cutoff: string;
  absent_cutoff: string;
  shift_end: string;
};

// A staff member's own shift wins; otherwise the shift_settings default.
// 0007's all-or-none constraint guarantees the override is complete.
async function resolveShift(staffMember: {
  shift_start: string | null;
  late_cutoff: string | null;
  absent_cutoff: string | null;
  shift_end: string | null;
}): Promise<Shift | null> {
  if (staffMember.shift_start && staffMember.late_cutoff &&
      staffMember.absent_cutoff && staffMember.shift_end) {
    return {
      shift_start: staffMember.shift_start,
      late_cutoff: staffMember.late_cutoff,
      absent_cutoff: staffMember.absent_cutoff,
      shift_end: staffMember.shift_end,
    };
  }
  const { data } = await supabase
    .from("shift_settings")
    .select("shift_start, late_cutoff, absent_cutoff, shift_end")
    .eq("id", 1)
    .maybeSingle();
  return data as Shift | null;
}

const supabase = createClient(
  Deno.env.get("SUPABASE_URL")!,
  Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
);

serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: corsHeaders });
  }

  try {
    const deviceKey = req.headers.get("x-device-key");
    if (deviceKey !== Deno.env.get("DEVICE_API_KEY")) {
      return json({ status: "error", message: "Unauthorized device" }, 401);
    }

    const { rfid_uid, otp, action } = await req.json();
    if (!rfid_uid || !otp) {
      return json(
        { status: "error", message: "rfid_uid and otp required" },
        400,
      );
    }

    const { data: staffMember, error: staffError } = await supabase
      .from("staff")
      .select(
        "id, first_name, middle_name, last_name, email, " +
          "shift_start, late_cutoff, absent_cutoff, shift_end",
      )
      .eq("rfid_uid", rfid_uid)
      .maybeSingle();
    if (staffError) throw staffError;

    if (!staffMember) {
      await logAudit(null, rfid_uid, "unregistered");
      return json({ status: "unregistered" });
    }

    const { data: token, error: tokenError } = await supabase
      .from("otp_tokens")
      .select("id, otp_code, expires_at, is_used")
      .eq("staff_id", staffMember.id)
      .order("created_at", { ascending: false })
      .limit(1)
      .maybeSingle();
    if (tokenError) throw tokenError;

    if (!token || token.is_used) {
      await logAudit(staffMember.id, rfid_uid, "invalid", "no active token");
      await flagIfTooManyFailures(staffMember.id, rfid_uid);
      return json({ status: "invalid" });
    }

    if (new Date(token.expires_at).getTime() < Date.now()) {
      await logAudit(staffMember.id, rfid_uid, "expired");
      return json({ status: "expired" });
    }

    if (token.otp_code !== otp) {
      await logAudit(staffMember.id, rfid_uid, "invalid", "otp mismatch");
      await flagIfTooManyFailures(staffMember.id, rfid_uid);
      return json({ status: "invalid" });
    }

    // Status-integrity check: the next event type is derived from the most
    // recent row for this staff member — never decided by the ESP32.
    const { data: lastEvent } = await supabase
      .from("attendance_events")
      .select("event_type")
      .eq("staff_id", staffMember.id)
      .order("event_timestamp", { ascending: false })
      .limit(1)
      .maybeSingle();

    const nextEventType: "TIME-IN" | "TIME-OUT" =
      !lastEvent || lastEvent.event_type === "TIME-OUT"
        ? "TIME-IN"
        : "TIME-OUT";

    // The device tells us which button was pressed. The server still decides
    // the event type above — this only checks the two agree, so the staff
    // member is told they pressed the wrong one instead of silently getting
    // the opposite event. Clients that omit `action` keep the old behaviour.
    if (action !== undefined && action !== null && action !== "") {
      if (action !== "TIME-IN" && action !== "TIME-OUT") {
        return json(
          { status: "error", message: "action must be TIME-IN or TIME-OUT" },
          400,
        );
      }
      if (action !== nextEventType) {
        // Deliberately before the token is consumed: a wrong button is a
        // slip, so the OTP stays valid and they can press the other one.
        await logAudit(
          staffMember.id,
          rfid_uid,
          "wrong_action",
          `pressed ${action}, expected ${nextEventType}`,
        );
        return json({ status: "wrong_action", expected: nextEventType });
      }
    }

    // Mark the token used so it can't be replayed.
    await supabase.from("otp_tokens").update({ is_used: true }).eq(
      "id",
      token.id,
    );

    // shift_settings times are Philippine wall-clock times, but the edge
    // runtime's clock is UTC — every comparison happens in the school's
    // timezone, never the runtime's.
    const nowMinutes = minutesSinceMidnightIn(SCHOOL_TIMEZONE);
    const isRestDay = isRestDayIn(SCHOOL_TIMEZONE);
    const shift = await resolveShift(staffMember);

    let isLate = false;
    let isAbsent = false;
    let overtimeMinutes = 0;
    let undertimeMinutes = 0;

    if (nextEventType === "TIME-IN") {
      // Rest days have no shift to be late for, so neither flag applies.
      if (shift && !isRestDay) {
        isLate = nowMinutes > toMinutes(shift.late_cutoff);
        // Past the absent cutoff the day is not credited, but the TIME-IN is
        // still recorded — they are physically here and must be able to
        // time out.
        isAbsent = nowMinutes > toMinutes(shift.absent_cutoff);
      }
    } else if (shift) {
      // TIME-OUT: measure the day against the shift. lastEvent is the
      // matching TIME-IN, so re-read it for its timestamp.
      const { data: timeIn } = await supabase
        .from("attendance_events")
        .select("event_timestamp, is_rest_day")
        .eq("staff_id", staffMember.id)
        .eq("event_type", "TIME-IN")
        .order("event_timestamp", { ascending: false })
        .limit(1)
        .maybeSingle();

      if (isRestDay || timeIn?.is_rest_day) {
        // Rest-day work is entirely overtime — there is no shift to fall
        // short of, so undertime stays 0.
        if (timeIn) {
          const workedMs = Date.now() -
            new Date(timeIn.event_timestamp).getTime();
          overtimeMinutes = Math.max(0, Math.round(workedMs / 60000));
        }
      } else {
        const shiftEnd = toMinutes(shift.shift_end);
        if (nowMinutes > shiftEnd) {
          overtimeMinutes = nowMinutes - shiftEnd;
        } else {
          undertimeMinutes = shiftEnd - nowMinutes;
        }
      }
    }

    const { error: insertError } = await supabase
      .from("attendance_events")
      .insert({
        staff_id: staffMember.id,
        event_type: nextEventType,
        is_late: isLate,
        is_absent: isAbsent,
        is_rest_day: isRestDay,
        overtime_minutes: overtimeMinutes,
        undertime_minutes: undertimeMinutes,
      });
    if (insertError) throw insertError;

    await logAudit(staffMember.id, rfid_uid, "success");

    return json({
      status: "success",
      event_type: nextEventType,
      staff_name: formatFullName(staffMember),
      is_late: isLate,
      is_absent: isAbsent,
      is_rest_day: isRestDay,
      overtime_minutes: overtimeMinutes,
      undertime_minutes: undertimeMinutes,
    });
  } catch (err) {
    console.error(err);
    return json({ status: "error", message: String(err) }, 500);
  }
});

// After MAX_FAILED_ATTEMPTS invalid verifies for the same staff member
// within FAILED_WINDOW_MINUTES, write an extra flagged audit row — this is
// the buddy-punching-attempt signal for the desktop app's anomaly log.
async function flagIfTooManyFailures(staffId: string, rfidUid: string) {
  const since = new Date(
    Date.now() - FAILED_WINDOW_MINUTES * 60 * 1000,
  ).toISOString();

  const { count } = await supabase
    .from("otp_audit_log")
    .select("id", { count: "exact", head: true })
    .eq("staff_id", staffId)
    .eq("action", "verify")
    .eq("result", "invalid")
    .gte("created_at", since);

  if ((count ?? 0) >= MAX_FAILED_ATTEMPTS) {
    await supabase.from("otp_audit_log").insert({
      staff_id: staffId,
      rfid_uid: rfidUid,
      action: "verify",
      result: "invalid",
      detail:
        `ANOMALY: ${count} failed OTP attempts in ${FAILED_WINDOW_MINUTES} min — possible buddy-punching attempt`,
    });
  }
}

async function logAudit(
  staffId: string | null,
  rfidUid: string,
  result: "success" | "invalid" | "expired" | "unregistered" | "wrong_action",
  detail?: string,
) {
  await supabase.from("otp_audit_log").insert({
    staff_id: staffId,
    rfid_uid: rfidUid,
    action: "verify",
    result,
    detail,
  });
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...corsHeaders, "Content-Type": "application/json" },
  });
}
