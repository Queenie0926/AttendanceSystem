using Attendance_System.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Attendance_System.Views
{
    public partial class LogsView : UserControl
    {
        private readonly List<AttendanceRecord> _allLogs = new()
        {
            new AttendanceRecord { Id = 1, StaffName = "Juan Dela Cruz", Date = DateTime.Today, Time = new TimeSpan(8, 2, 0), Status = "Time In" },
            new AttendanceRecord { Id = 2, StaffName = "Juan Dela Cruz", Date = DateTime.Today, Time = new TimeSpan(17, 5, 0), Status = "Time Out" },
            new AttendanceRecord { Id = 3, StaffName = "Maria Santos", Date = DateTime.Today, Time = new TimeSpan(8, 15, 0), Status = "Time In" },
            new AttendanceRecord { Id = 4, StaffName = "Maria Santos", Date = DateTime.Today, Time = new TimeSpan(17, 30, 0), Status = "Time Out" },
            new AttendanceRecord { Id = 5, StaffName = "Juan Dela Cruz", Date = DateTime.Today.AddDays(-1), Time = new TimeSpan(8, 0, 0), Status = "Time In" },
            new AttendanceRecord { Id = 6, StaffName = "Juan Dela Cruz", Date = DateTime.Today.AddDays(-1), Time = new TimeSpan(17, 10, 0), Status = "Time Out" },
        };

        public LogsView()
        {
            InitializeComponent();
            LogsGrid.ItemsSource = _allLogs;
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            string status = (CmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            var from = DateFrom.SelectedDate;
            var to = DateTo.SelectedDate;
            string staffFilter = TxtStaffFilter.Text.Trim();

            var filtered = _allLogs.Where(log =>
                (!from.HasValue || log.Date.Date >= from.Value.Date) &&
                (!to.HasValue || log.Date.Date <= to.Value.Date) &&
                (string.IsNullOrEmpty(staffFilter) || log.StaffName.Contains(staffFilter, StringComparison.OrdinalIgnoreCase)) &&
                (status == "All" || log.Status == status)
            ).ToList();

            LogsGrid.ItemsSource = filtered;
        }
    }
}