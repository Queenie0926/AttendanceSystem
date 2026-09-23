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

        [Column("department")]
        public string Department { get; set; } = "";

        [Column("program")]
        public string Program { get; set; } = "";

        [Column("position_role")]
        public string PositionRole { get; set; } = "";

        [Column("rfid_uid")]
        public string RfidUid { get; set; } = "";

        // Own shift ("HH:mm:ss"); all null means the default shift_settings row applies.
        [Column("shift_start")]
        public string? ShiftStart { get; set; }

        [Column("late_cutoff")]
        public string? LateCutoff { get; set; }

        [Column("absent_cutoff")]
        public string? AbsentCutoff { get; set; }

        [Column("shift_end")]
        public string? ShiftEnd { get; set; }

        [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
        public DateTimeOffset CreatedAt { get; set; }
    }
}
