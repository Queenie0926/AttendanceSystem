namespace Attendance_System.Models
{
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

        public string FullName =>
            string.Join(" ", new[] { FirstName, MiddleName, LastName }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
