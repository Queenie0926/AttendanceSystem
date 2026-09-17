namespace Attendance_System.Models
{
    public record StaffShift(TimeSpan Start, TimeSpan LateCutoff, TimeSpan AbsentCutoff);

    public class StaffRecord
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = "";
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Department { get; set; } = "";
        public string Program { get; set; } = "";
        public string PositionRole { get; set; } = "";
        public string RfidUid { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        /// <summary>Null when the staff member follows the default shift.</summary>
        public StaffShift? Shift { get; set; }

        public string ShiftDisplay => Shift is null
            ? "Default"
            : $"{DateTime.Today.Add(Shift.Start):h:mm tt}";

        public string FullName =>
            string.Join(" ", new[] { FirstName, MiddleName, LastName }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
