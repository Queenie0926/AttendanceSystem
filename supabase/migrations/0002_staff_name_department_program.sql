-- Staff enrollment: structured name + department/program/position enums.
--
-- PROTOTYPE NOTE: this assumes the `staff` table is empty. If you enrolled
-- test staff under the old schema (single `name` + free-text `position`),
-- delete those rows first — the new columns are NOT NULL with no backfill.

create type staff_department as enum ('College of Engineering Education');

create type staff_program as enum (
  'Civil Engineering',
  'Chemical Engineering',
  'Computer Engineering',
  'Electrical Engineering',
  'Electronics and Electrical Engineering',
  'Mechanical Engineering'
);

create type staff_position as enum ('Dean', 'Assistant Dean', 'Program Head', 'Faculty Member');

alter table staff drop column name;
alter table staff drop column position;

alter table staff
  add column first_name    text not null,
  add column middle_name   text,
  add column last_name     text not null,
  add column department    staff_department not null default 'College of Engineering Education',
  add column program       staff_program not null,
  add column position_role staff_position not null;
