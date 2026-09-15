using Postgrest.Attributes;
using Postgrest.Models;

namespace Attendance_System.Data.Entities
{
    [Table("staff")]
    public class StaffEntity : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("first_name")]
        public string FirstName { get; set; } = "";

        [Column("middle_name")]
        public string? MiddleName { get; set; }

        [Column("last_name")]
        public string LastName { get; set; } = "";

        [Column("email")]
        public string Email { get; set; } = "";

        // Only "College of Engineering Education" for this prototype.
        [Column("department")]
        public string Department { get; set; } = "";

        [Column("program")]
        public string Program { get; set; } = "";

        // "Dean" | "Assistant Dean" | "Program Head" | "Faculty Member"
        [Column("position_role")]
        public string PositionRole { get; set; } = "";

        [Column("rfid_uid")]
        public string RfidUid { get; set; } = "";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
