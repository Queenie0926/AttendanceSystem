-- ── wrong_action audit result ───────────────────────────────────────────────
-- verify-otp now compares the button the staff member pressed against the
-- event type the server derives. A mismatch is its own audit result: logging
-- it as 'invalid' would inflate the buddy-punching counter in
-- flagIfTooManyFailures(), which only counts result = 'invalid'. Pressing the
-- wrong button is a user slip, not a spoofing signal.

alter type otp_result add value if not exists 'wrong_action';
