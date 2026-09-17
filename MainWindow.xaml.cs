using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Attendance_System.Services;
using Attendance_System.Views;

namespace Attendance_System
{
    public partial class MainWindow : Window
    {
        private bool _isSidebarExpanded = true;
        private const double ExpandedWidth = 240;
        private const double CollapsedWidth = 72;

        /// <summary>True when the window closed because the admin signed out (App shows login again).</summary>
        public bool SignedOut { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            var service = SupabaseService.Instance;
            string email = service.CurrentUserEmail ?? "";
            string displayName = email.Contains('@') ? email[..email.IndexOf('@')] : email;
            TxtAccountName.Text = displayName;
            AccountAvatar.Content = displayName;
            TxtAccountRole.Text = service.CurrentRole switch
            {
                "admin" => "Administrator",
                "hr" => "HR",
                "viewer" => "Viewer",
                _ => "No role",
            };
            TxtToday.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");

            // Hide screens the role can't use. RLS enforces the same rules server-side.
            BtnEnrollment.Visibility = service.CanManageStaff ? Visibility.Visible : Visibility.Collapsed;
            BtnShift.Visibility = service.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
            BtnUsers.Visibility = service.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

            Navigate("Dashboard");
        }

        private void ToggleNav_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarExpanded = !_isSidebarExpanded;

            var animation = new DoubleAnimation
            {
                To = _isSidebarExpanded ? ExpandedWidth : CollapsedWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            // Hide labels immediately when collapsing; restore after expanding.
            if (!_isSidebarExpanded)
                SetLabelsVisible(false);
            else
                animation.Completed += (s, args) => SetLabelsVisible(true);

            // Chevron points left to collapse, right to expand.
            ToggleGlyph.Data = Geometry.Parse(_isSidebarExpanded ? "M 10 3 L 5 8 L 10 13" : "M 6 3 L 11 8 L 6 13");

            SidebarBorder.BeginAnimation(WidthProperty, animation);
        }

        private void SetLabelsVisible(bool visible)
        {
            var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            LblDashboard.Visibility = visibility;
            LblEnrollment.Visibility = visibility;
            LblLogs.Visibility = visibility;
            LblReports.Visibility = visibility;
            LblShift.Visibility = visibility;
            LblUsers.Visibility = visibility;
            BrandText.Visibility = visibility;
            NavHeading.Visibility = visibility;
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: string target })
                Navigate(target);
        }

        private void Navigate(string target)
        {
            MainContent.Content = target switch
            {
                "Dashboard" => new DashboardView(),
                "Enrollment" => new EnrollmentView(),
                "Reports" => new ReportsView(),
                "Shift" => new ShiftSettingsView(),
                "Users" => new UsersView(),
                _ => new LogsView(),
            };

            // NavButtonStyle lights up the button whose Tag is "Active".
            BtnDashboard.Tag = target == "Dashboard" ? "Active" : null;
            BtnEnrollment.Tag = target == "Enrollment" ? "Active" : null;
            BtnLogs.Tag = target == "Logs" ? "Active" : null;
            BtnReports.Tag = target == "Reports" ? "Active" : null;
            BtnShift.Tag = target == "Shift" ? "Active" : null;
            BtnUsers.Tag = target == "Users" ? "Active" : null;
        }

        private void Account_Click(object sender, RoutedEventArgs e)
        {
            var email = new MenuItem { Header = SupabaseService.Instance.CurrentUserEmail, IsEnabled = false };
            var signOut = new MenuItem { Header = "Sign out", Style = (Style)FindResource("DangerMenuItemStyle") };
            signOut.Click += SignOut_Click;

            var menu = new ContextMenu
            {
                PlacementTarget = BtnAccount,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            };
            menu.Items.Add(email);
            menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparatorStyle") });
            menu.Items.Add(signOut);
            menu.IsOpen = true;
        }

        private async void SignOut_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(this, "Sign out of Staffly?", "Sign out",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await SupabaseService.Instance.SignOutAsync();
            }
            catch (Exception ex)
            {
                // The local session is cleared either way; a failed server call shouldn't trap the user.
                System.Diagnostics.Debug.WriteLine($"Sign-out error: {ex}");
            }

            SignedOut = true;
            Close();
        }
    }
}
