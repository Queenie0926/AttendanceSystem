using Attendance_System.Models;
using Attendance_System.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Attendance_System.Views
{
    public partial class DashboardView : UserControl
    {
        // Taps arrive from the terminal while the dashboard is open, so keep it current.
        private readonly DispatcherTimer _autoRefresh = new() { Interval = TimeSpan.FromMinutes(1) };
        private bool _loading;

        public DashboardView()
        {
            InitializeComponent();

            _autoRefresh.Tick += async (s, e) => await LoadAsync();
            Loaded += async (s, e) =>
            {
                _autoRefresh.Start();
                await LoadAsync();
            };
            Unloaded += (s, e) => _autoRefresh.Stop();
            ((FrameworkElement)PresentBar.Parent).SizeChanged += (s, e) => UpdatePresentBar();
        }

        private async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            BtnRefresh.IsEnabled = false;
            TxtUpdated.Text = "Updating…";

            try
            {
                var today = DateTime.Today;
                var staffTask = SupabaseService.Instance.GetStaffAsync();
                var eventsTask = SupabaseService.Instance.GetAttendanceEventsAsync(today.AddDays(-6), today);
                await Task.WhenAll(staffTask, eventsTask);

                Show(DashboardStats.Build(staffTask.Result.Count, eventsTask.Result, today));
                TxtError.Visibility = Visibility.Collapsed;
                TxtUpdated.Text = $"Updated {DateTime.Now:h:mm tt}";
            }
            catch (Exception ex)
            {
                TxtError.Text = $"Couldn't load the dashboard: {ex.Message}";
                TxtError.Visibility = Visibility.Visible;
                TxtUpdated.Text = "";
            }
            finally
            {
                BtnRefresh.IsEnabled = true;
                _loading = false;
            }
        }

        /// <summary>Binds the stats to the page. Public so previews can render sample data.</summary>
        public void Show(DashboardStats stats)
        {
            DataContext = stats;
            RecentEmpty.Visibility = stats.RecentActivity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DrawAxis(stats.ChartMax);
            UpdatePresentBar();
        }

        private void UpdatePresentBar()
        {
            if (DataContext is DashboardStats stats && PresentBar.Parent is FrameworkElement track)
                PresentBar.Width = track.ActualWidth * stats.PresentRatio;
        }

        // Horizontal gridlines with count labels, aligned to the bars' 180px plot area.
        private void DrawAxis(int max)
        {
            GridLines.Children.Clear();
            AxisLabels.Children.Clear();
            const double top = 22;   // room reserved above the plot for value labels

            for (int i = 0; i <= 4; i++)
            {
                double y = top + DashboardStats.ChartPlotHeight * (4 - i) / 4;

                var line = new Border { Style = (Style)FindResource("GridLineStyle") };
                line.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = GridLines });
                Canvas.SetTop(line, y);
                GridLines.Children.Add(line);

                var label = new TextBlock
                {
                    Text = (max * i / 4).ToString(),
                    Style = (Style)FindResource("AxisLabelStyle"),
                    Width = 20,
                    TextAlignment = TextAlignment.Right,
                };
                Canvas.SetTop(label, y - 8);
                AxisLabels.Children.Add(label);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    }
}
