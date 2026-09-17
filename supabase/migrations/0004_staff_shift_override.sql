-- ── Per-staff shift ─────────────────────────────────────────────────────────
-- Not everyone works the same hours. A staff member with all three columns
-- set uses their own shift; otherwise shift_settings (id = 1) is the default.

alter table staff
  add column shift_start   time,
  add column late_cutoff   time,
  add column absent_cutoff time,
  add constraint staff_shift_all_or_none check (
    (shift_start is null and late_cutoff is null and absent_cutoff is null) or
    (shift_start is not null and late_cutoff is not null and absent_cutoff is not null)
  ),
  add constraint staff_shift_order check (
    shift_start is null or (late_cutoff >= shift_start and absent_cutoff > late_cutoff)
  );
