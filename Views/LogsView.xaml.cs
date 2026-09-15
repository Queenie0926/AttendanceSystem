using Attendance_System.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
            BtnApply.IsEnabled = false;
            LoadingText.Visibility = Visibility.Visible;
            try
            {
                LogsGrid.ItemsSource = await SupabaseService.Instance.GetAttendanceEventsAsync(from, to, staffFilter, statusFilter);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't load attendance logs:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnApply.IsEnabled = true;
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private async void FilterButton_Click(object sender, RoutedEventArgs e) => await ApplyFiltersAsync();

        private async void TxtStaffFilter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await ApplyFiltersAsync();
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            DateFrom.SelectedDate = null;
            DateTo.SelectedDate = null;
            TxtStaffFilter.Clear();
            CmbStatus.SelectedIndex = 0;
            await LoadLogsAsync();
        }

        private Task ApplyFiltersAsync()
        {
            string status = (CmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            return LoadLogsAsync(DateFrom.SelectedDate, DateTo.SelectedDate, TxtStaffFilter.Text.Trim(), status);
        }
    }
}
