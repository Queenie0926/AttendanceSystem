namespace Attendance_System.Models
{
    public class AttendanceRecord
    {
        public Guid Id { get; set; }
        public Guid StaffId { get; set; }
        public string StaffName { get; set; } = "";
        public DateTime Date { get; set; }
        public TimeSpan Time { get; set; }
        public string Status { get; set; } = ""; // "Time In" or "Time Out"
        public bool IsLate { get; set; }
        public bool IsAbsent { get; set; }
        public bool IsRestDay { get; set; }
        public int OvertimeMinutes { get; set; }
        public int UndertimeMinutes { get; set; }

        public string DateDisplay => Date.ToString("MM/dd/yyyy");
        public string TimeDisplay => DateTime.Today.Add(Time).ToString("hh:mm tt");
        public string RelativeDateDisplay =>
            Date.Date == DateTime.Today ? "Today"
            : Date.Date == DateTime.Today.AddDays(-1) ? "Yesterday"
            : Date.ToString("MMM d");
        // Absent outranks late: someone past the absent cutoff was also late,
        // but the day is not credited, so that is what the admin needs to see.
        public string StatusDisplay =>
            IsAbsent ? $"{Status} (Absent)"
            : IsLate ? $"{Status} (Late)"
            : Status;

        public string OvertimeDisplay => FormatMinutes(OvertimeMinutes);
        public string UndertimeDisplay => FormatMinutes(UndertimeMinutes);

        public static string FormatMinutes(int minutes)
        {
            if (minutes <= 0) return "--";
            var h = minutes / 60;
            var m = minutes % 60;
            return h == 0 ? $"{m}m" : m == 0 ? $"{h}h" : $"{h}h {m}m";
        }
    }

    public class DtrEntry
    {
        public string StaffName { get; set; } = "";
        public DateTime Date { get; set; }
        public string TimeIn { get; set; } = "--";
        public string TimeOut { get; set; } = "--";
        public string HoursWorked { get; set; } = "--";
        public bool IsLate { get; set; }
        public bool IsAbsent { get; set; }
        public bool IsRestDay { get; set; }
        public int OvertimeMinutes { get; set; }
        public int UndertimeMinutes { get; set; }

        public string DateDisplay => Date.ToString("MM/dd/yyyy");
        public string OvertimeDisplay => AttendanceRecord.FormatMinutes(OvertimeMinutes);
        public string UndertimeDisplay => AttendanceRecord.FormatMinutes(UndertimeMinutes);

        // Saturday and Sunday are rest days: no late or absent, and every
        // hour worked is overtime.
        public string Remarks =>
            IsRestDay ? (OvertimeMinutes > 0 ? "Rest day (OT)" : "Rest day")
            : IsAbsent ? "Absent"
            : IsLate ? "Late"
            : "";
    }
}
