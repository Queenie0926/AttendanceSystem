-- RFID + OTP Attendance System — initial schema
-- Run via `supabase db push`, or paste into the Supabase SQL editor.

create extension if not exists pgcrypto;

-- ── Enums ──────────────────────────────────────────────────────────────────

create type event_type as enum ('TIME-IN', 'TIME-OUT');
create type otp_action as enum ('send', 'verify');
create type otp_result as enum ('success', 'invalid', 'expired', 'cooldown', 'unregistered');

-- ── staff ──────────────────────────────────────────────────────────────────
-- One row per employee/faculty member. rfid_uid is how the ESP32 identifies
-- who tapped; email is where OTPs are sent.

create table staff (
  id         uuid primary key default gen_random_uuid(),
  name       text not null,
  email      text not null,
  position   text,
  rfid_uid   text not null unique,
  created_at timestamptz not null default now()
);

-- ── shift_settings ─────────────────────────────────────────────────────────
-- Single-row config table, admin-editable from the desktop app. Read on
-- every TIME-IN to decide on-time / late / absent.

create table shift_settings (
  id           int primary key default 1,
  shift_start  time not null default '08:00',
  late_cutoff  time not null default '08:15',
  absent_cutoff time not null default '10:00',
  updated_at   timestamptz not null default now(),
  constraint single_row check (id = 1)
);

insert into shift_settings (id) values (1);

-- ── attendance_events ──────────────────────────────────────────────────────
-- The attendance log. Status-integrity: the backend always looks at the most
-- recent row for a given staff_id to decide whether the next tap is a
-- TIME-IN or TIME-OUT — the ESP32 never decides this locally.

create table attendance_events (
  id              uuid primary key default gen_random_uuid(),
  staff_id        uuid not null references staff(id) on delete cascade,
  event_type      event_type not null,
  event_timestamp timestamptz not null default now(),
  is_late         boolean not null default false,
  created_at      timestamptz not null default now()
);

create index idx_attendance_staff_time on attendance_events (staff_id, event_timestamp desc);

-- ── otp_tokens ─────────────────────────────────────────────────────────────
-- Short-lived OTP codes. Never exposed to any client except the edge
-- functions (service_role only — no RLS policy grants client access).

create table otp_tokens (
  id         uuid primary key default gen_random_uuid(),
  staff_id   uuid not null references staff(id) on delete cascade,
  otp_code   text not null,
  created_at timestamptz not null default now(),
  expires_at timestamptz not null,
  is_used    boolean not null default false
);

create index idx_otp_staff_created on otp_tokens (staff_id, created_at desc);

-- ── otp_audit_log ──────────────────────────────────────────────────────────
-- Security/audit trail: every send/verify attempt gets a row. A burst of
-- 'invalid' verify results for the same staff_id is the buddy-punching
-- signal (flagged by the verify-otp edge function).

create table otp_audit_log (
  id         uuid primary key default gen_random_uuid(),
  staff_id   uuid references staff(id) on delete set null,
  rfid_uid   text,
  action     otp_action not null,
  result     otp_result not null,
  detail     text,
  created_at timestamptz not null default now()
);

create index idx_audit_staff_created on otp_audit_log (staff_id, created_at desc);

-- ── Row Level Security ─────────────────────────────────────────────────────
-- Edge functions use the service_role key, which bypasses RLS entirely —
-- these policies only govern the desktop app / a future web dashboard
-- reading via the anon/authenticated key.

alter table staff enable row level security;
alter table attendance_events enable row level security;
alter table otp_tokens enable row level security;
alter table otp_audit_log enable row level security;
alter table shift_settings enable row level security;

create policy "Authenticated read staff" on staff
  for select using (auth.role() = 'authenticated');
create policy "Authenticated write staff" on staff
  for all using (auth.role() = 'authenticated') with check (auth.role() = 'authenticated');

create policy "Authenticated read attendance" on attendance_events
  for select using (auth.role() = 'authenticated');

create policy "Authenticated read audit log" on otp_audit_log
  for select using (auth.role() = 'authenticated');

create policy "Authenticated read shift settings" on shift_settings
  for select using (auth.role() = 'authenticated');
create policy "Authenticated update shift settings" on shift_settings
  for update using (auth.role() = 'authenticated');

-- otp_tokens intentionally has no client-facing policy: only service_role
-- (the edge functions) may read or write it.
