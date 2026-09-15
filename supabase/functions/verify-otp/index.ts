// verify-otp
//
// Called by the ESP32 after the staff member enters the OTP on the keypad.
// Validates the code, then performs the server-side status-integrity check
// (TIME-IN vs TIME-OUT) and logs the attendance event. Also does late
// detection against shift_settings, and flags a possible buddy-punching
// attempt after repeated invalid OTP verifies.
//
// Request:  POST { rfid_uid: string, otp: string }
// Headers:  x-device-key: <DEVICE_API_KEY>
// Response: { status: "success" | "invalid" | "expired" | "unregistered" | "error", ... }

import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";
import { corsHeaders } from "../_shared/cors.ts";
import { formatFullName } from "../_shared/staff.ts";

const MAX_FAILED_ATTEMPTS = 3; // failed verifies within the window below
const FAILED_WINDOW_MINUTES = 10;

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

    const { rfid_uid, otp } = await req.json();
    if (!rfid_uid || !otp) {
      return json(
        { status: "error", message: "rfid_uid and otp required" },
        400,
      );
    }

    const { data: staffMember, error: staffError } = await supabase
      .from("staff")
      .select("id, first_name, middle_name, last_name, email")
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

    // Mark the token used so it can't be replayed.
    await supabase.from("otp_tokens").update({ is_used: true }).eq(
      "id",
      token.id,
    );

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

    // Late detection only applies to TIME-IN.
    let isLate = false;
    if (nextEventType === "TIME-IN") {
      const { data: settings } = await supabase
        .from("shift_settings")
        .select("late_cutoff")
        .eq("id", 1)
        .maybeSingle();

      if (settings) {
        const [lh, lm] = settings.late_cutoff.split(":").map(Number);
        const now = new Date();
        const lateCutoff = new Date(now);
        lateCutoff.setHours(lh, lm, 0, 0);
        isLate = now > lateCutoff;
      }
    }

    const { error: insertError } = await supabase
      .from("attendance_events")
      .insert({
        staff_id: staffMember.id,
        event_type: nextEventType,
        is_late: isLate,
      });
    if (insertError) throw insertError;

    await logAudit(staffMember.id, rfid_uid, "success");

    return json({
      status: "success",
      event_type: nextEventType,
      staff_name: formatFullName(staffMember),
      is_late: isLate,
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
  result: "success" | "invalid" | "expired" | "unregistered",
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
