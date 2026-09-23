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
        public DateTimeOffset EventTimestamp { get; set; }

        [Column("is_late")]
        public bool IsLate { get; set; }

        // Arrived after the absent cutoff on a regular workday. TIME-IN rows only.
        [Column("is_absent")]
        public bool IsAbsent { get; set; }

        // Saturday or Sunday in Asia/Manila - no late or absent is flagged,
        // and all hours worked are credited as overtime.
        [Column("is_rest_day")]
        public bool IsRestDay { get; set; }

        // TIME-OUT rows only: minutes past shift_end, or every minute worked
        // on a rest day.
        [Column("overtime_minutes")]
        public int OvertimeMinutes { get; set; }

        // TIME-OUT rows only: minutes of the shift left unworked. Always 0
        // on a rest day.
        [Column("undertime_minutes")]
        public int UndertimeMinutes { get; set; }

        [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
        public DateTimeOffset CreatedAt { get; set; }
    }
}
