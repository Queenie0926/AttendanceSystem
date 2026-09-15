using Attendance_System.Models;
using Attendance_System.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Attendance_System.Views
{
    public partial class StaffDetailsWindow : Window
    {
        private readonly StaffRecord _staff;

        public StaffDetailsWindow(StaffRecord staff)
        {
            InitializeComponent();
            _staff = staff;

            TxtInitials.Text = $"{Initial(staff.FirstName)}{Initial(staff.LastName)}";
            TxtFullName.Text = staff.FullName;
            TxtRole.Text = staff.PositionRole;
            TxtDepartment.Text = staff.Department;
            TxtProgram.Text = staff.Program;
            TxtEmail.Text = staff.Email;
            TxtRfid.Text = staff.RfidUid;
            TxtEnrolled.Text = staff.CreatedAt == default ? "—" : staff.CreatedAt.ToString("MMMM d, yyyy");

            Loaded += async (s, e) => await LoadLastActivityAsync();
        }

        private async Task LoadLastActivityAsync()
        {
            try
            {
                var last = await SupabaseService.Instance.GetLastEventAsync(_staff.Id);
                if (last is null)
                {
                    TxtLastActivity.Text = "No taps recorded yet";
                    return;
                }

                // Same colours as the Attendance Logs status badges.
                var fill = (IValueConverter)FindResource("StatusBadgeFill");
                var text = (IValueConverter)FindResource("StatusBadgeText");
                LastBadge.Background = (Brush)fill.Convert(last, typeof(Brush), null, CultureInfo.CurrentCulture);
                TxtLastStatus.Foreground = (Brush)text.Convert(last, typeof(Brush), null, CultureInfo.CurrentCulture);
                TxtLastStatus.Text = last.StatusDisplay;
                LastBadge.Visibility = Visibility.Visible;
                TxtLastActivity.Text = $"{last.DateDisplay} · {last.TimeDisplay}";
            }
            catch (Exception ex)
            {
                TxtLastActivity.Text = $"Couldn't load: {ex.Message}";
            }
        }

        private static string Initial(string? name) =>
            string.IsNullOrWhiteSpace(name) ? "" : char.ToUpperInvariant(name.Trim()[0]).ToString();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
