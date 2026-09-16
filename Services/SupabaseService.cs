using Attendance_System.Config;
using Attendance_System.Data.Entities;
using Attendance_System.Models;
using static Postgrest.Constants;
using Supabase;
using Supabase.Gotrue;

using SupabaseClient = Supabase.Client;

namespace Attendance_System.Services
{
    /// <summary>
    /// Thin wrapper around the Supabase C# client: auth, staff CRUD,
    /// attendance log reads, and shift settings. All RLS-protected reads go
    /// through a signed-in admin session (see LoginWindow).
    /// </summary>
    public sealed class SupabaseService
    {
        private static SupabaseService? _instance;
        public static SupabaseService Instance => _instance ??= new SupabaseService();

        private SupabaseClient? _client;
        private SupabaseClient RequireClient() =>
            _client ?? throw new InvalidOperationException("SupabaseService.InitializeAsync() was not called.");

        public bool IsSignedIn => _client?.Auth.CurrentSession != null;

        private SupabaseService() { }

        public async Task InitializeAsync()
        {
            if (_client != null) return;

            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false,
            };

            _client = new SupabaseClient(AppConfig.SupabaseUrl, AppConfig.SupabaseAnonKey, options);
            await _client.InitializeAsync();
        }

        public string? CurrentUserEmail => _client?.Auth.CurrentUser?.Email;

        /// <summary>Role from the signed-in user's profiles row; null if they have none.</summary>
        public string? CurrentRole { get; private set; }

        public bool IsAdmin => CurrentRole == UserRoles.Admin;
        public bool CanManageStaff => CurrentRole is UserRoles.Admin or UserRoles.Hr;

        public async Task<Session?> SignInAsync(string email, string password)
        {
            var client = RequireClient();
            return await client.Auth.SignInWithPassword(email, password);
        }

        public async Task SignOutAsync()
        {
            var client = RequireClient();
            CurrentRole = null;
            await client.Auth.SignOut();
        }

        // ── Profiles & roles ─────────────────────────────────────────────

        /// <summary>Reads the signed-in user's own profile and caches their role.</summary>
        public async Task<string?> LoadCurrentRoleAsync()
        {
            var client = RequireClient();
            var userId = client.Auth.CurrentUser?.Id;
            CurrentRole = null;
            if (userId is null) return null;

            var id = Guid.Parse(userId);
            var response = await client.From<ProfileEntity>().Where(x => x.Id == id).Get();
            CurrentRole = response.Models.FirstOrDefault()?.Role;
            return CurrentRole;
        }

        public async Task<List<UserProfileRecord>> GetProfilesAsync()
        {
            var client = RequireClient();
            var response = await client.From<ProfileEntity>()
                .Order(x => x.Email!, Ordering.Ascending)
                .Get();

            var currentId = client.Auth.CurrentUser?.Id;
            return response.Models.Select(p =>
            {
                var record = new UserProfileRecord
                {
                    Id = p.Id,
                    Email = p.Email ?? "",
                    IsCurrentUser = p.Id.ToString() == currentId,
                    FullName = p.FullName ?? "",
                    Role = p.Role,
                };
                record.MarkSaved();
                return record;
            }).ToList();
        }

        public async Task UpdateProfileAsync(Guid id, string fullName, string role)
        {
            var client = RequireClient();
            var response = await client.From<ProfileEntity>()
                .Where(x => x.Id == id)
                .Set(x => x.FullName!, string.IsNullOrWhiteSpace(fullName) ? null! : fullName.Trim())
                .Set(x => x.Role, role)
                .Update();

            // RLS silently filters out rows the caller may not update.
            if (response.Models.Count == 0)
                throw new InvalidOperationException("Update was not applied — only admins can change user profiles.");
        }

        // ── Staff ────────────────────────────────────────────────────────

        public async Task<List<StaffRecord>> GetStaffAsync()
        {
            var client = RequireClient();
            var response = await client.From<StaffEntity>()
                .Order(x => x.LastName, Ordering.Ascending)
                .Get();

            return response.Models.Select(ToRecord).ToList();
        }

        public async Task<StaffRecord> AddStaffAsync(
            string firstName, string? middleName, string lastName, string email,
            string department, string program, string positionRole, string rfidUid)
        {
            var client = RequireClient();
            var entity = new StaffEntity
            {
                FirstName = firstName,
                MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName,
                LastName = lastName,
                Email = email,
                Department = department,
                Program = program,
                PositionRole = positionRole,
                RfidUid = rfidUid,
            };

            var response = await client.From<StaffEntity>().Insert(entity);
            return ToRecord(response.Models.First());
        }

        public async Task<StaffRecord> UpdateStaffAsync(
            Guid staffId, string firstName, string? middleName, string lastName, string email,
            string department, string program, string positionRole, string rfidUid)
        {
            var client = RequireClient();
            var response = await client.From<StaffEntity>()
                .Where(x => x.Id == staffId)
                .Set(x => x.FirstName, firstName)
                .Set(x => x.MiddleName!, string.IsNullOrWhiteSpace(middleName) ? null! : middleName)
                .Set(x => x.LastName, lastName)
                .Set(x => x.Email, email)
                .Set(x => x.Department, department)
                .Set(x => x.Program, program)
                .Set(x => x.PositionRole, positionRole)
                .Set(x => x.RfidUid, rfidUid)
                .Update();

            return ToRecord(response.Models.FirstOrDefault()
                ?? throw new InvalidOperationException("Staff member not found — they may have been removed."));
        }

        /// <summary>
        /// Deletes a staff member. Their attendance_events and otp_tokens rows
        /// are removed by ON DELETE CASCADE; audit log rows keep a null staff_id.
        /// </summary>
        public async Task DeleteStaffAsync(Guid staffId)
        {
            var client = RequireClient();
            await client.From<StaffEntity>().Where(x => x.Id == staffId).Delete();
        }

        public async Task<AttendanceRecord?> GetLastEventAsync(Guid staffId)
        {
            var client = RequireClient();
            var response = await client.From<AttendanceEventEntity>()
                .Where(x => x.StaffId == staffId)
                .Order(x => x.EventTimestamp, Ordering.Descending)
                .Limit(1)
                .Get();

            var last = response.Models.FirstOrDefault();
            return last is null ? null : ToRecord(last, new Dictionary<Guid, StaffEntity>());
        }

        private static StaffRecord ToRecord(StaffEntity e) => new()
        {
            CreatedAt = e.CreatedAt.LocalDateTime,
            Id = e.Id,
            FirstName = e.FirstName,
            MiddleName = e.MiddleName,
            LastName = e.LastName,
            Email = e.Email,
            Department = e.Department,
            Program = e.Program,
            PositionRole = e.PositionRole,
            RfidUid = e.RfidUid,
        };

        // ── Attendance ───────────────────────────────────────────────────

        public async Task<List<AttendanceRecord>> GetAttendanceEventsAsync(
            DateTime? from = null, DateTime? to = null, string? staffNameFilter = null, string? statusFilter = null)
        {
            var client = RequireClient();

            Postgrest.Interfaces.IPostgrestTable<AttendanceEventEntity> query = client.From<AttendanceEventEntity>();

            if (from.HasValue)
            {
                var fromUtc = from.Value.Date.ToUniversalTime();
                query = query.Filter("event_timestamp", Operator.GreaterThanOrEqual, fromUtc.ToString("o"));
            }
            if (to.HasValue)
            {
                var toUtc = to.Value.Date.AddDays(1).ToUniversalTime();
                query = query.Filter("event_timestamp", Operator.LessThan, toUtc.ToString("o"));
            }

            var response = await query.Order(x => x.EventTimestamp, Ordering.Descending).Get();

            var staffLookup = await GetStaffLookupAsync();

            var records = response.Models.Select(e => ToRecord(e, staffLookup)).ToList();

            if (!string.IsNullOrWhiteSpace(staffNameFilter))
                records = records.Where(r => r.StaffName.Contains(staffNameFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "All")
                records = records.Where(r => r.Status == statusFilter).ToList();

            return records;
        }

        private async Task<Dictionary<Guid, StaffEntity>> GetStaffLookupAsync()
        {
            var client = RequireClient();
            var response = await client.From<StaffEntity>().Get();
            return response.Models.ToDictionary(s => s.Id);
        }

        private static AttendanceRecord ToRecord(AttendanceEventEntity e, Dictionary<Guid, StaffEntity> staffLookup)
        {
            var staffName = staffLookup.TryGetValue(e.StaffId, out var staff) ? ToRecord(staff).FullName : "(unknown)";
            // DateTimeOffset keeps the UTC offset, so this converts exactly once.
            var localTime = e.EventTimestamp.LocalDateTime;

            return new AttendanceRecord
            {
                Id = e.Id,
                StaffName = staffName,
                Date = localTime.Date,
                Time = localTime.TimeOfDay,
                Status = e.EventType == "TIME-IN" ? "Time In" : "Time Out",
                IsLate = e.IsLate,
            };
        }

        // ── Shift settings ───────────────────────────────────────────────

        public async Task<ShiftSettingsEntity> GetShiftSettingsAsync()
        {
            var client = RequireClient();
            var response = await client.From<ShiftSettingsEntity>()
                .Where(x => x.Id == 1)
                .Single();

            return response ?? new ShiftSettingsEntity { Id = 1 };
        }

        public async Task<ShiftSettingsEntity> UpdateShiftSettingsAsync(TimeSpan shiftStart, TimeSpan lateCutoff, TimeSpan absentCutoff)
        {
            var client = RequireClient();
            var response = await client.From<ShiftSettingsEntity>()
                .Where(x => x.Id == 1)
                .Set(x => x.ShiftStart, shiftStart.ToString(@"hh\:mm\:ss"))
                .Set(x => x.LateCutoff, lateCutoff.ToString(@"hh\:mm\:ss"))
                .Set(x => x.AbsentCutoff, absentCutoff.ToString(@"hh\:mm\:ss"))
                .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow)
                .Update();

            return response.Models.FirstOrDefault()
                ?? throw new InvalidOperationException("Shift settings row (id = 1) was not found.");
        }
    }
}
