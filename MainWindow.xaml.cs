using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Attendance_System.Views;

namespace Attendance_System
{
    public partial class MainWindow : Window
    {
        private bool _isSidebarExpanded = true;
        private const double ExpandedWidth = 220;
        private const double CollapsedWidth = 64;

        public MainWindow()
        {
            InitializeComponent();
            MainContent.Content = new LogsView(); // default landing view
            SetActiveIndicator(IndLogs);
        }

        private void ToggleNav_Click(object sender, RoutedEventArgs e)
        {
            double targetWidth = _isSidebarExpanded ? CollapsedWidth : ExpandedWidth;
            _isSidebarExpanded = !_isSidebarExpanded;

            var animation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = new Duration(System.TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            // Fade labels out immediately when collapsing; fade back in after expansion finishes.
            if (!_isSidebarExpanded)
            {
                SetLabelsVisible(false);
            }
            else
            {
                animation.Completed += (s, args) => SetLabelsVisible(true);
            }

            SidebarBorder.BeginAnimation(WidthProperty, animation);
        }

        private void SetLabelsVisible(bool visible)
        {
            var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            LblEnrollment.Visibility = visibility;
            LblLogs.Visibility = visibility;
            LblReports.Visibility = visibility;

            // Logo and school name/app name hide together with the labels —
            // only icons remain when the sidebar is collapsed.
            LogoPanel.Visibility = visibility;
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender != Button.MouseDoubleClickEvent && sender is Button clicked)
            {
                switch (clicked.Tag as string)
                {
                    case "Enrollment":
                        MainContent.Content = new EnrollmentView();
                        SetActiveIndicator(IndEnrollment);
                        break;
                    case "Logs":
                        MainContent.Content = new LogsView();
                        SetActiveIndicator(IndLogs);
                        break;
                    case "Reports":
                        MainContent.Content = new ReportsView();
                        SetActiveIndicator(IndReports);
                        break;
                }
            }
        }

        private void SetActiveIndicator(Border active)
        {
            IndEnrollment.Visibility = Visibility.Collapsed;
            IndLogs.Visibility = Visibility.Collapsed;
            IndReports.Visibility = Visibility.Collapsed;
            active.Visibility = Visibility.Visible;
        }
    }
}