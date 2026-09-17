using Attendance_System.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Attendance_System.Views
{
    public partial class LogsView : UserControl
    {
        // Suppresses reloads while Reset clears several filters at once.
        private bool _suspendReload = true;

        public LogsView()
        {
            InitializeComponent();
            Loaded += async (s, e) =>
            {
                _suspendReload = false;
                await ApplyFiltersAsync();
            };
        }

        private async Task LoadLogsAsync(DateTime? from, DateTime? to, string? staffFilter, string? statusFilter)
        {
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
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private async void StatusTab_Checked(object sender, RoutedEventArgs e) => await ApplyFiltersAsync();

        private async void Date_Changed(object? sender, SelectionChangedEventArgs e) => await ApplyFiltersAsync();

        private async void TxtStaffFilter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await ApplyFiltersAsync();
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _suspendReload = true;
            DateFrom.SelectedDate = null;
            DateTo.SelectedDate = null;
            TxtStaffFilter.Clear();
            TabAll.IsChecked = true;
            _suspendReload = false;
            await ApplyFiltersAsync();
        }

        private Task ApplyFiltersAsync()
        {
            if (_suspendReload) return Task.CompletedTask;

            string status = TabIn.IsChecked == true ? "Time In" : TabOut.IsChecked == true ? "Time Out" : "All";
            return LoadLogsAsync(DateFrom.SelectedDate, DateTo.SelectedDate, TxtStaffFilter.Text.Trim(), status);
        }
    }
}
