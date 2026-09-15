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

        public async Task<Session?> SignInAsync(string email, string password)
        {
            var client = RequireClient();
            return await client.Auth.SignInWithPassword(email, password);
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

        private static StaffRecord ToRecord(StaffEntity e) => new()
        {
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
                query = query.Where(x => x.EventTimestamp >= fromUtc);
            }
            if (to.HasValue)
            {
                var toUtc = to.Value.Date.AddDays(1).ToUniversalTime();
                query = query.Where(x => x.EventTimestamp < toUtc);
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
            var localTime = e.EventTimestamp.ToLocalTime();

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

            return response ?? new ShiftSettingsEntity();
        }
    }
}
