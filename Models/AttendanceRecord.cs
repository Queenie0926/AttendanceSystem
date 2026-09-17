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

        public string DateDisplay => Date.ToString("MM/dd/yyyy");
        public string TimeDisplay => DateTime.Today.Add(Time).ToString("hh:mm tt");
        public string RelativeDateDisplay =>
            Date.Date == DateTime.Today ? "Today"
            : Date.Date == DateTime.Today.AddDays(-1) ? "Yesterday"
            : Date.ToString("MMM d");
        public string StatusDisplay => IsLate ? $"{Status} (Late)" : Status;
    }

    public class DtrEntry
    {
        public string StaffName { get; set; } = "";
        public DateTime Date { get; set; }
        public string TimeIn { get; set; } = "--";
        public string TimeOut { get; set; } = "--";
        public string HoursWorked { get; set; } = "--";
        public bool IsLate { get; set; }

        public string DateDisplay => Date.ToString("MM/dd/yyyy");
        public string Remarks => IsLate ? "Late" : "";
    }
}
