using Postgrest.Attributes;
using Postgrest.Models;

namespace Attendance_System.Data.Entities
{
    // Single-row config table (id is always 1). Postgres `time` columns come
    // back as "HH:mm:ss" strings — parsed to TimeSpan by the service layer.
    [Table("shift_settings")]
    public class ShiftSettingsEntity : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("shift_start")]
        public string ShiftStart { get; set; } = "08:00:00";

        [Column("late_cutoff")]
        public string LateCutoff { get; set; } = "08:15:00";

        [Column("absent_cutoff")]
        public string AbsentCutoff { get; set; } = "10:00:00";

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
