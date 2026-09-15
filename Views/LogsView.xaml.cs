using Attendance_System.Services;
using System.Windows;
using System.Windows.Controls;

namespace Attendance_System.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView()
        {
            InitializeComponent();
            Loaded += async (s, e) => await LoadLogsAsync();
        }

        private async Task LoadLogsAsync(
            DateTime? from = null, DateTime? to = null, string? staffFilter = null, string? statusFilter = null)
        {
            try
            {
                var logs = await SupabaseService.Instance.GetAttendanceEventsAsync(from, to, staffFilter, statusFilter);
                LogsGrid.ItemsSource = logs;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load attendance logs:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            string status = (CmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            var from = DateFrom.SelectedDate;
            var to = DateTo.SelectedDate;
            string staffFilter = TxtStaffFilter.Text.Trim();

            await LoadLogsAsync(from, to, staffFilter, status);
        }
    }
}
