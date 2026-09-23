using Attendance_System.Models;
using Attendance_System.Services;
using Microsoft.Win32;
using System.Globalization;
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

        private void TxtStaffFilter_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) GenerateButton_Click(sender, e);
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

                entry.IsRestDay = log.IsRestDay;

                if (log.Status == "Time In" && entry.TimeIn == "--")
                {
                    entry.TimeIn = DateTime.Today.Add(log.Time).ToString("hh:mm tt");
                    entry.IsLate = log.IsLate;
                    entry.IsAbsent = log.IsAbsent;
                }
                else if (log.Status == "Time Out")
                {
                    entry.TimeOut = DateTime.Today.Add(log.Time).ToString("hh:mm tt");
                    // Overtime and undertime are computed server-side on the
                    // TIME-OUT row against that staff member's shift.
                    entry.OvertimeMinutes = log.OvertimeMinutes;
                    entry.UndertimeMinutes = log.UndertimeMinutes;
                }
            }

            AddNoShowDays(grouped, logs, from, to);

            foreach (var entry in grouped.Values)
            {
                if (entry.TimeIn != "--" && entry.TimeOut != "--" &&
                    DateTime.TryParse(entry.TimeIn, out var tIn) &&
                    DateTime.TryParse(entry.TimeOut, out var tOut))
                {
                    var worked = tOut - tIn;
                    // InvariantCulture: on a comma-decimal locale "8.50" would
                    // format as "8,50" and split the CSV column in two.
                    entry.HoursWorked = worked.TotalHours > 0
                        ? worked.TotalHours.ToString("0.00", CultureInfo.InvariantCulture) + " hrs"
                        : "--";
                }
            }

            _currentReport = grouped.Values
                .OrderBy(x => x.StaffName)
                .ThenBy(x => x.Date)
                .ToList();

            DtrGrid.ItemsSource = _currentReport;
            BtnExport.IsEnabled = _currentReport.Count > 0;
        }

        /// <summary>
        /// A staff member who never taps never reaches the server, so absence
        /// from a no-show can only be derived here. For every staff member who
        /// appears anywhere in the range, fill in the Mon–Fri days they have no
        /// TIME-IN for. Saturday and Sunday are rest days and are skipped —
        /// not working on a rest day is not an absence.
        /// </summary>
        private static void AddNoShowDays(
            Dictionary<(string, DateTime), DtrEntry> grouped,
            List<AttendanceRecord> logs, DateTime from, DateTime to)
        {
            var staffNames = logs.Select(l => l.StaffName).Distinct().ToList();
            // Never mark today absent — the workday is still running.
            var lastDay = to > DateTime.Today ? DateTime.Today : to;

            foreach (var name in staffNames)
            {
                for (var day = from.Date; day < lastDay.Date.AddDays(1); day = day.AddDays(1))
                {
                    if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                    if (day >= DateTime.Today) continue;
                    if (grouped.ContainsKey((name, day))) continue;

                    grouped[(name, day)] = new DtrEntry
                    {
                        StaffName = name,
                        Date = day,
                        IsAbsent = true,
                    };
                }
            }
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
            sb.AppendLine("Staff Name,Date,Time In,Time Out,Hours Worked,Overtime,Undertime,Remarks");
            foreach (var row in _currentReport)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(row.StaffName),
                    Csv(row.DateDisplay),
                    Csv(row.TimeIn),
                    Csv(row.TimeOut),
                    Csv(row.HoursWorked),
                    Csv(row.OvertimeDisplay),
                    Csv(row.UndertimeDisplay),
                    Csv(row.Remarks),
                }));
            }

            try
            {
                // UTF-8 *with* BOM: without it Excel opens the file as the
                // system codepage and mangles names like Muñoz.
                File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            }
            catch (IOException ex)
            {
                // Usually the file is still open in Excel.
                MessageBox.Show($"Couldn't write the file:\n{ex.Message}\n\n" +
                    "If it's open in Excel, close it and export again.",
                    "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show($"Couldn't write the file:\n{ex.Message}",
                    "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("Report exported successfully.", "Done",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Quotes a CSV field when it contains a comma, quote or line break,
        /// doubling any embedded quotes. A name like "Santos, Jr." would
        /// otherwise split into two columns and shift every later field.
        /// </summary>
        private static string Csv(string? value)
        {
            value ??= "";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
