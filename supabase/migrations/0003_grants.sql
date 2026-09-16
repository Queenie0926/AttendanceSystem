-- Table/schema privileges for the API roles.
-- RLS policies (0001) only filter rows; the role still needs GRANTs to touch
-- the schema at all. Without these, PostgREST returns 42501
-- "permission denied for schema public".

grant usage on schema public to anon, authenticated, service_role;

-- Desktop app signs in, so it runs as `authenticated`; RLS still decides
-- which rows/operations are allowed (otp_tokens has no policy → no access).
grant select, insert, update, delete on all tables in schema public to authenticated;
grant usage, select on all sequences in schema public to authenticated;

-- Edge functions use the service_role key (bypasses RLS, but still needs grants).
grant all on all tables in schema public to service_role;
grant all on all sequences in schema public to service_role;
grant execute on all functions in schema public to service_role, authenticated;

alter default privileges in schema public
  grant select, insert, update, delete on tables to authenticated;
alter default privileges in schema public
  grant usage, select on sequences to authenticated;
alter default privileges in schema public
  grant all on tables to service_role;
alter default privileges in schema public
  grant all on sequences to service_role;
