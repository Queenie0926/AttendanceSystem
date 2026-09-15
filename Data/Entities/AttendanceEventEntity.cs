using Postgrest.Attributes;
using Postgrest.Models;

namespace Attendance_System.Data.Entities
{
    [Table("attendance_events")]
    public class AttendanceEventEntity : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("staff_id")]
        public Guid StaffId { get; set; }

        //time-in or time-out
        [Column("event_type")]
        public string EventType { get; set; } = "";

        [Column("event_timestamp")]
        public DateTime EventTimestamp { get; set; }

        [Column("is_late")]
        public bool IsLate { get; set; }

        [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
        public DateTime CreatedAt { get; set; }
    }
}
