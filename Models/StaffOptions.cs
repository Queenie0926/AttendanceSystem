namespace Attendance_System.Models
{
    /// <summary>
    /// Static lookup lists backing the Department/Program/Position dropdowns.
    /// Mirrors the staff_department / staff_program / staff_position enums
    /// in the Supabase schema (supabase/migrations/0002_*.sql).
    /// </summary>
    public static class StaffOptions
    {
        public static readonly string[] Departments =
        {
            "College of Engineering Education",
        };

        public static readonly Dictionary<string, string[]> ProgramsByDepartment = new()
        {
            ["College of Engineering Education"] = new[]
            {
                "Civil Engineering",
                "Chemical Engineering",
                "Computer Engineering",
                "Electrical Engineering",
                "Electronics and Electrical Engineering",
                "Mechanical Engineering",
            },
        };

        public static readonly string[] Positions =
        {
            "Dean",
            "Assistant Dean",
            "Program Head",
            "Faculty Member",
        };
    }
}
