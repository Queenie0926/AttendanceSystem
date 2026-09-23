-- ── Shift end, overtime / undertime, absence ────────────────────────────────
-- Adds the end of the workday so the backend can tell overtime from
-- undertime, and records the result on the TIME-OUT row.
--
-- Policy encoded here and in verify-otp:
--   * Monday–Friday are the only regular workdays. Late and absent are never
--     flagged on Saturday or Sunday.
--   * Weekend work is credited entirely as overtime — there is no shift to be
--     early or late for on a rest day.
--   * Arriving after absent_cutoff marks the day absent (the staff member is
--     physically present and the TIME-IN is still recorded, but the day is
--     not credited). The default moves to 12:00: miss more than half of an
--     08:00–17:00 shift and it is an absence.

alter table shift_settings
  add column shift_end time not null default '17:00';

alter table shift_settings
  alter column absent_cutoff set default '12:00';

-- Existing installs keep whatever the admin set, but the untouched seed row
-- still carries the old 10:00 default — move it to the new policy.
update shift_settings
   set absent_cutoff = '12:00'
 where id = 1 and absent_cutoff = '10:00';

alter table shift_settings
  add constraint shift_settings_order check (
    late_cutoff >= shift_start
    and absent_cutoff > late_cutoff
    and shift_end > absent_cutoff
  );

-- ── Per-staff override gains the same column ────────────────────────────────
-- The all-or-none constraint has to be replaced, not added to: a staff member
-- with a custom shift must now specify all four times.

alter table staff
  add column shift_end time;

alter table staff
  drop constraint staff_shift_all_or_none,
  drop constraint staff_shift_order;

alter table staff
  add constraint staff_shift_all_or_none check (
    (shift_start is null and late_cutoff is null
      and absent_cutoff is null and shift_end is null)
    or
    (shift_start is not null and late_cutoff is not null
      and absent_cutoff is not null and shift_end is not null)
  ),
  add constraint staff_shift_order check (
    shift_start is null or (
      late_cutoff >= shift_start
      and absent_cutoff > late_cutoff
      and shift_end > absent_cutoff
    )
  );

-- Any staff member who already had a custom shift predates shift_end and
-- would now violate all-or-none. Give them the default end of day.
update staff
   set shift_end = (select shift_end from shift_settings where id = 1)
 where shift_start is not null and shift_end is null;

-- ── Attendance rows carry the computed result ───────────────────────────────
-- is_absent is set on the TIME-IN row; the minute counts are set on the
-- TIME-OUT row, since neither can be known until the staff member leaves.

alter table attendance_events
  add column is_absent         boolean not null default false,
  add column overtime_minutes  int     not null default 0,
  add column undertime_minutes int     not null default 0,
  add column is_rest_day       boolean not null default false,
  add constraint attendance_minutes_nonneg check (
    overtime_minutes >= 0 and undertime_minutes >= 0
  );

comment on column attendance_events.is_absent is
  'TIME-IN rows only: arrived after the absent cutoff on a regular workday.';
comment on column attendance_events.overtime_minutes is
  'TIME-OUT rows only: minutes worked past shift_end, or all minutes on a rest day.';
comment on column attendance_events.undertime_minutes is
  'TIME-OUT rows only: minutes of the shift left unworked. Always 0 on a rest day.';
comment on column attendance_events.is_rest_day is
  'Event fell on Saturday or Sunday in Asia/Manila.';

-- ── Keep the admin-only shift guard in step ─────────────────────────────────
-- 0005 enumerates the shift columns by name, so shift_end has to be added or
-- a non-admin could set it.

create or replace function enforce_staff_shift_admin_only()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  if auth.uid() is null then
    return new;
  end if;

  if (tg_op = 'INSERT' and (new.shift_start is not null or new.late_cutoff is not null
                         or new.absent_cutoff is not null or new.shift_end is not null))
     or (tg_op = 'UPDATE' and (new.shift_start   is distinct from old.shift_start
                            or new.late_cutoff   is distinct from old.late_cutoff
                            or new.absent_cutoff is distinct from old.absent_cutoff
                            or new.shift_end     is distinct from old.shift_end)) then
    if not exists (select 1 from profiles where id = auth.uid() and role = 'admin') then
      raise exception 'Only admins can change a staff member''s shift.'
        using errcode = '42501';
    end if;
  end if;

  return new;
end;
$$;
