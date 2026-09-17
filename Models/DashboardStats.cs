namespace Attendance_System.Models
{
    public class DayAttendance
    {
        public DateTime Date { get; init; }
        public int OnTime { get; init; }
        public int Late { get; init; }
        public int Present => OnTime + Late;

        public string Label => Date.Date == DateTime.Today ? "Today" : Date.ToString("ddd");
        public string DateLabel => Date.ToString("MMM d");
        public string ToolTipText => $"{Date:dddd, MMM d}: {Present} present ({OnTime} on time, {Late} late)";

        public double OnTimeHeight { get; set; }
        public double LateHeight { get; set; }
    }

    public class DashboardStats
    {
        public const double ChartPlotHeight = 180;

        public int TotalStaff { get; private init; }
        public int PresentToday { get; private init; }
        public int LateToday { get; private init; }
        public int OnSiteNow { get; private init; }
        public int TapsToday { get; private init; }
        public List<DayAttendance> Week { get; private init; } = new();
        public int ChartMax { get; private init; }
        public List<AttendanceRecord> RecentActivity { get; private init; } = new();

        public int NotInYet => Math.Max(0, TotalStaff - PresentToday);
        public string PresentRateText => TotalStaff == 0 ? "No staff enrolled" : $"{Percent(PresentToday, TotalStaff)}% of staff";
        public string LateRateText => PresentToday == 0 ? "No arrivals yet" : $"{Percent(LateToday, PresentToday)}% of arrivals";
        public string NotInYetText => $"{NotInYet} not in yet";
        public string OnSiteText => OnSiteNow == 1 ? "1 person timed in, not out" : $"{OnSiteNow} timed in, not out";
        public double PresentRatio => TotalStaff == 0 ? 0 : (double)PresentToday / TotalStaff;

        public static DashboardStats Build(int totalStaff, IReadOnlyList<AttendanceRecord> lastSevenDays, DateTime today)
        {
            today = today.Date;
            var timeIns = lastSevenDays.Where(r => r.Status == "Time In").ToList();

            var week = Enumerable.Range(0, 7).Select(offset =>
            {
                var day = today.AddDays(offset - 6);
                // First TIME-IN per staff member decides on-time vs late for the day.
                var firstIns = timeIns.Where(r => r.Date.Date == day)
                    .GroupBy(r => r.StaffId)
                    .Select(g => g.OrderBy(r => r.Time).First())
                    .ToList();
                return new DayAttendance
                {
                    Date = day,
                    OnTime = firstIns.Count(r => !r.IsLate),
                    Late = firstIns.Count(r => r.IsLate),
                };
            }).ToList();

            // Round the axis up to a friendly number so gridline labels are whole.
            int peak = Math.Max(week.Max(d => d.Present), totalStaff > 0 ? Math.Min(totalStaff, 4) : 4);
            int step = Math.Max(1, (int)Math.Ceiling(peak / 4.0));
            int chartMax = step * 4;
            foreach (var d in week)
            {
                d.OnTimeHeight = ChartPlotHeight * d.OnTime / chartMax;
                d.LateHeight = ChartPlotHeight * d.Late / chartMax;
            }

            var todays = lastSevenDays.Where(r => r.Date.Date == today).ToList();
            // Latest event per staff member today; TIME-IN means they haven't timed out yet.
            int onSite = todays.GroupBy(r => r.StaffId)
                .Count(g => g.OrderByDescending(r => r.Time).First().Status == "Time In");

            return new DashboardStats
            {
                TotalStaff = totalStaff,
                PresentToday = week[^1].Present,
                LateToday = week[^1].Late,
                OnSiteNow = onSite,
                TapsToday = todays.Count,
                Week = week,
                ChartMax = chartMax,
                RecentActivity = lastSevenDays.OrderByDescending(r => r.Date.Date.Add(r.Time)).Take(6).ToList(),
            };
        }

        private static int Percent(int part, int whole) => (int)Math.Round(100.0 * part / whole);
    }
}
