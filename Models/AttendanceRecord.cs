using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Attendance_System.Models
{
    public class AttendanceRecord
    {
        public int Id { get; set; }
        public string StaffName { get; set; } = "";
        public DateTime Date { get; set; }
        public TimeSpan Time { get; set; }
        public string Status { get; set; } = ""; // "Time In" or "Time Out"

        public string DateDisplay => Date.ToString("MM/dd/yyyy");
        public string TimeDisplay => DateTime.Today.Add(Time).ToString("hh:mm tt");
    }

    public class DtrEntry
    {
        public string StaffName { get; set; } = "";
        public DateTime Date { get; set; }
        public string TimeIn { get; set; } = "--";
        public string TimeOut { get; set; } = "--";
        public string HoursWorked { get; set; } = "--";

        public string DateDisplay => Date.ToString("MM/dd/yyyy");
    }
}
