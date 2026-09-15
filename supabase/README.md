# Supabase backend

Replaces the original PHP/XAMPP/MySQL backend. Two edge functions handle the
OTP + status-integrity flow; the ESP32 calls them directly over HTTPS.

## Setup

1. Install the Supabase CLI and link this repo to your project:
   ```bash
   supabase login
   supabase link --project-ref <your-project-ref>
   ```

2. Apply the schema:
   ```bash
   supabase db push
   ```
   (or paste `migrations/0001_init.sql` into the Supabase SQL editor)

3. Set secrets used by the edge functions:
   ```bash
   supabase secrets set DEVICE_API_KEY=<a-long-random-string>
   supabase secrets set GMAIL_USER=<your-gmail-address>
   supabase secrets set GMAIL_APP_PASSWORD=<gmail-app-password>
   ```
   - `DEVICE_API_KEY` — shared secret the ESP32 sends as `x-device-key` on
     every request. Generate one with `openssl rand -hex 32` and flash it
     into the firmware.
   - `GMAIL_APP_PASSWORD` — a Gmail **App Password** (requires 2FA enabled on
     the Gmail account), not the account's normal login password.

4. Deploy the functions. `--no-verify-jwt` is required because the ESP32
   authenticates with `x-device-key`, not a Supabase user session:
   ```bash
   supabase functions deploy send-otp --no-verify-jwt
   supabase functions deploy verify-otp --no-verify-jwt
   ```

## Endpoints

Base URL: `https://<project-ref>.functions.supabase.co`

### `POST /send-otp`
Headers: `x-device-key: <DEVICE_API_KEY>`
```json
{ "rfid_uid": "A1B2C3D4" }
```
Responses: `{"status":"sent","staff_name":"..."}` ·
`{"status":"cooldown","wait_seconds":42}` ·
`{"status":"unregistered"}`

### `POST /verify-otp`
Headers: `x-device-key: <DEVICE_API_KEY>`
```json
{ "rfid_uid": "A1B2C3D4", "otp": "123456" }
```
Responses: `{"status":"success","event_type":"TIME-IN","staff_name":"...","is_late":false}` ·
`{"status":"expired"}` · `{"status":"invalid"}` · `{"status":"unregistered"}`

## Desktop app admin account

The C# desktop app signs in via Supabase Auth (email/password) so its requests
count as `authenticated` for RLS. Create at least one admin user manually:

Supabase Dashboard → Authentication → Users → **Add user** (set a password,
skip email confirmation for internal use). Use that email/password to sign
into the desktop app's login screen.

## Notes

- Both functions use the `service_role` key server-side and bypass RLS —
  `x-device-key` is the only gate on who can call them. Keep it secret; it's
  equivalent to a device password.
- `otp_tokens` has no client-facing RLS policy at all — only these functions
  can ever read or write it.
- The live dashboard (planned) doesn't need a `get-status.php` equivalent —
  it can subscribe to `attendance_events` directly via Supabase Realtime
  from the `authenticated` role.
- Local testing: `supabase functions serve` runs both functions locally
  against your linked project's database.
