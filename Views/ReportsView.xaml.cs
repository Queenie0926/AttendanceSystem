using Attendance_System.Models;
using Attendance_System.Services;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Attendance_System.Views
{
    public partial class ReportsView : UserControl
    {
        private List<DtrEntry> _currentReport = new();

        public ReportsView()
        {
            InitializeComponent();
            DateFrom.SelectedDate = DateTime.Today.AddDays(-7);
            DateTo.SelectedDate = DateTime.Today;
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (DateFrom.SelectedDate is null || DateTo.SelectedDate is null)
            {
                MessageBox.Show("Please select both a start and end date.", "Missing Dates",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var from = DateFrom.SelectedDate.Value.Date;
            var to = DateTo.SelectedDate.Value.Date;
            string staffFilter = TxtStaffFilter.Text.Trim();

            List<AttendanceRecord> logs;
            try
            {
                logs = await SupabaseService.Instance.GetAttendanceEventsAsync(from, to, staffFilter);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load attendance data:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var grouped = new Dictionary<(string, DateTime), DtrEntry>();
            foreach (var log in logs.OrderBy(l => l.Date).ThenBy(l => l.Time))
            {
                var key = (log.StaffName, log.Date.Date);
                if (!grouped.TryGetValue(key, out var entry))
                {
                    entry = new DtrEntry { StaffName = log.StaffName, Date = log.Date.Date };
                    grouped[key] = entry;
                }

                if (log.Status == "Time In" && entry.TimeIn == "--")
                {
                    entry.TimeIn = DateTime.Today.Add(log.Time).ToString("hh:mm tt");
                    entry.IsLate = log.IsLate;
                }
                else if (log.Status == "Time Out")
                {
                    entry.TimeOut = DateTime.Today.Add(log.Time).ToString("hh:mm tt");
                }
            }

            foreach (var entry in grouped.Values)
            {
                if (entry.TimeIn != "--" && entry.TimeOut != "--" &&
                    DateTime.TryParse(entry.TimeIn, out var tIn) &&
                    DateTime.TryParse(entry.TimeOut, out var tOut))
                {
                    var worked = tOut - tIn;
                    entry.HoursWorked = worked.TotalHours > 0 ? $"{worked.TotalHours:0.00} hrs" : "--";
                }
            }

            _currentReport = grouped.Values
                .OrderBy(x => x.StaffName)
                .ThenBy(x => x.Date)
                .ToList();

            DtrGrid.ItemsSource = _currentReport;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReport.Count == 0)
            {
                MessageBox.Show("Generate a report first before exporting.", "Nothing to Export",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV file (*.csv)|*.csv",
                FileName = $"DTR_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("Staff Name,Date,Time In,Time Out,Hours Worked,Remarks");
            foreach (var row in _currentReport)
                sb.AppendLine($"{row.StaffName},{row.DateDisplay},{row.TimeIn},{row.TimeOut},{row.HoursWorked},{row.Remarks}");

            File.WriteAllText(dialog.FileName, sb.ToString());
            MessageBox.Show("Report exported successfully.", "Done",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
