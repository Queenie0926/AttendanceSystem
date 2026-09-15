// send-otp
//
// Called by the ESP32 right after an RFID tap. Looks up the staff member by
// rfid_uid, enforces a cooldown, generates a 6-digit OTP, stores it, and
// emails it to the staff member's registered address via Gmail SMTP.
//
// Request:  POST { rfid_uid: string }
// Headers:  x-device-key: <DEVICE_API_KEY>
// Response: { status: "sent" | "cooldown" | "unregistered" | "error", ... }

import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";
import { SMTPClient } from "https://deno.land/x/denomailer@1.6.0/mod.ts";
import { corsHeaders } from "../_shared/cors.ts";
import { formatFullName } from "../_shared/staff.ts";

const OTP_EXPIRY_SECONDS = 90;
const OTP_COOLDOWN_SECONDS = 60;

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

    const { rfid_uid } = await req.json();
    if (!rfid_uid) {
      return json({ status: "error", message: "rfid_uid required" }, 400);
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

    // Cooldown: reject if the most recent OTP for this staff member is
    // younger than OTP_COOLDOWN_SECONDS.
    const { data: lastToken } = await supabase
      .from("otp_tokens")
      .select("created_at")
      .eq("staff_id", staffMember.id)
      .order("created_at", { ascending: false })
      .limit(1)
      .maybeSingle();

    if (lastToken) {
      const secondsSince =
        (Date.now() - new Date(lastToken.created_at).getTime()) / 1000;
      if (secondsSince < OTP_COOLDOWN_SECONDS) {
        const wait = Math.ceil(OTP_COOLDOWN_SECONDS - secondsSince);
        await logAudit(staffMember.id, rfid_uid, "cooldown", `wait ${wait}s`);
        return json({ status: "cooldown", wait_seconds: wait });
      }
    }

    // Invalidate any still-unused token before issuing a new one — only one
    // live OTP per person at a time.
    await supabase
      .from("otp_tokens")
      .update({ is_used: true })
      .eq("staff_id", staffMember.id)
      .eq("is_used", false);

    const otp = generateOtp();
    const expiresAt = new Date(
      Date.now() + OTP_EXPIRY_SECONDS * 1000,
    ).toISOString();

    const { error: insertError } = await supabase.from("otp_tokens").insert({
      staff_id: staffMember.id,
      otp_code: otp,
      expires_at: expiresAt,
    });
    if (insertError) throw insertError;

    const fullName = formatFullName(staffMember);
    await sendOtpEmail(staffMember.email, fullName, otp);
    await logAudit(staffMember.id, rfid_uid, "success");

    return json({ status: "sent", staff_name: fullName });
  } catch (err) {
    console.error(err);
    return json({ status: "error", message: String(err) }, 500);
  }
});

function generateOtp(): string {
  const buf = new Uint32Array(1);
  crypto.getRandomValues(buf);
  return (buf[0] % 1_000_000).toString().padStart(6, "0");
}

async function sendOtpEmail(email: string, name: string, otp: string) {
  const client = new SMTPClient({
    connection: {
      hostname: "smtp.gmail.com",
      port: 465,
      tls: true,
      auth: {
        username: Deno.env.get("GMAIL_USER")!,
        password: Deno.env.get("GMAIL_APP_PASSWORD")!,
      },
    },
  });

  await client.send({
    from: Deno.env.get("GMAIL_USER")!,
    to: email,
    subject: "Your Attendance OTP Code",
    content:
      `Hi ${name},\n\n` +
      `Your one-time code is: ${otp}\n` +
      `It expires in ${OTP_EXPIRY_SECONDS} seconds.\n\n` +
      `If you did not tap your RFID card, you can ignore this email.`,
  });

  await client.close();
}

async function logAudit(
  staffId: string | null,
  rfidUid: string,
  result: "success" | "cooldown" | "unregistered",
  detail?: string,
) {
  await supabase.from("otp_audit_log").insert({
    staff_id: staffId,
    rfid_uid: rfidUid,
    action: "send",
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
