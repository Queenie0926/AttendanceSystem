-- ── Only admins may set a staff member's shift ─────────────────────────────
-- HR can still add and edit staff, but the shift columns are admin-only.
-- auth.uid() is null for service_role (edge functions) and the SQL editor,
-- so those are allowed.

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

  if (tg_op = 'INSERT' and (new.shift_start is not null or new.late_cutoff is not null or new.absent_cutoff is not null))
     or (tg_op = 'UPDATE' and (new.shift_start   is distinct from old.shift_start
                            or new.late_cutoff   is distinct from old.late_cutoff
                            or new.absent_cutoff is distinct from old.absent_cutoff)) then
    if not exists (select 1 from profiles where id = auth.uid() and role = 'admin') then
      raise exception 'Only admins can change a staff member''s shift.'
        using errcode = '42501';
    end if;
  end if;

  return new;
end;
$$;

create trigger staff_shift_admin_only
  before insert or update on staff
  for each row execute function enforce_staff_shift_admin_only();
