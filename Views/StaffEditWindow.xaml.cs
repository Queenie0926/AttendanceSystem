using Attendance_System.Models;
using Attendance_System.Services;
using System.Windows;
using System.Windows.Controls;
using TimeOption = Attendance_System.Views.ShiftSettingsView.TimeOption;

namespace Attendance_System.Views
{
    public partial class StaffEditWindow : Window
    {
        /// <summary>Null when enrolling a new staff member.</summary>
        private readonly StaffRecord? _original;
        private readonly IReadOnlyCollection<StaffRecord> _existing;

        /// <summary>The saved record, set when the dialog closes with DialogResult = true.</summary>
        public StaffRecord? Updated { get; private set; }

        private readonly List<TimeOption> _times =
            Enumerable.Range(0, 24 * 4).Select(i => new TimeOption(TimeSpan.FromMinutes(i * 15))).ToList();

        private StaffShift? _defaultShift;

        /// <param name="staff">The staff member to edit, or null to enroll a new one.</param>
        /// <param name="existing">Everyone already enrolled, to catch a reused RFID card before saving.</param>
        public StaffEditWindow(StaffRecord? staff, IReadOnlyCollection<StaffRecord> existing)
        {
            InitializeComponent();
            _original = staff;
            _existing = existing;

            CmbDepartment.ItemsSource = StaffOptions.Departments;
            CmbPosition.ItemsSource = StaffOptions.Positions;
            CmbShiftStart.ItemsSource = _times;
            CmbLateCutoff.ItemsSource = _times;
            CmbAbsentCutoff.ItemsSource = _times;

            if (staff is null)
            {
                Title = TxtTitle.Text = "Enroll staff";
                TxtSubtitle.Text = "Register a staff member and link their RFID card. One-time codes go to their email.";
                BtnSave.Content = "Enroll staff";
                CmbDepartment.SelectedIndex = 0;   // only one department in this prototype
                CmbShiftMode.SelectedIndex = 0;
            }
            else
            {
                TxtSubtitle.Text = $"Changes apply to {staff.FullName}'s next tap.";
                TxtFirstName.Text = staff.FirstName;
                TxtMiddleName.Text = staff.MiddleName ?? "";
                TxtLastName.Text = staff.LastName;
                TxtEmail.Text = staff.Email;
                TxtRfidUid.Text = staff.RfidUid;

                CmbDepartment.SelectedItem = staff.Department;   // fills CmbProgram via SelectionChanged
                CmbProgram.SelectedItem = staff.Program;
                CmbPosition.SelectedItem = staff.PositionRole;

                if (staff.Shift is { } shift) SelectShift(shift);
                CmbShiftMode.SelectedIndex = staff.Shift is null ? 0 : 1;
            }

            // Shift is admin-only (also enforced by a database trigger); others just see it.
            bool canEditShift = SupabaseService.Instance.IsAdmin;
            CmbShiftMode.IsEnabled = canEditShift;
            CustomShiftPanel.IsEnabled = canEditShift;

            Loaded += async (s, e) =>
            {
                TxtFirstName.Focus();
                await LoadDefaultShiftAsync();
            };
        }

        private async Task LoadDefaultShiftAsync()
        {
            try
            {
                var d = await SupabaseService.Instance.GetShiftSettingsAsync();
                _defaultShift = new StaffShift(ParseTime(d.ShiftStart), ParseTime(d.LateCutoff), ParseTime(d.AbsentCutoff));
                if (_original?.Shift is null) SelectShift(_defaultShift);   // starting point when switching to Custom
                UpdateShiftHelp();
            }
            catch
            {
                // Help text stays generic; saving doesn't depend on the default.
            }
        }

        private void CmbDepartment_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbDepartment.SelectedItem is string department &&
                StaffOptions.ProgramsByDepartment.TryGetValue(department, out var programs))
            {
                CmbProgram.ItemsSource = programs;
                CmbProgram.SelectedIndex = -1;
            }
        }

        private void CmbShiftMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CustomShiftPanel.Visibility = CmbShiftMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            UpdateShiftHelp();
        }

        private void UpdateShiftHelp()
        {
            string suffix = SupabaseService.Instance.IsAdmin ? "" : " Only admins can change this.";
            TxtShiftHelp.Text = (CmbShiftMode.SelectedIndex == 1
                ? "Only this staff member uses these times."
                : _defaultShift is { } d
                    ? $"Follows Shift Settings: starts {Fmt(d.Start)}, late after {Fmt(d.LateCutoff)}."
                    : "Follows the times in Shift Settings.") + suffix;
        }

        private void SelectShift(StaffShift shift)
        {
            CmbShiftStart.SelectedItem = OptionFor(shift.Start);
            CmbLateCutoff.SelectedItem = OptionFor(shift.LateCutoff);
            CmbAbsentCutoff.SelectedItem = OptionFor(shift.AbsentCutoff);
        }

        // Times off the 15-minute grid (e.g. edited in Supabase) still show up.
        private TimeOption OptionFor(TimeSpan value)
        {
            var match = _times.FirstOrDefault(o => o.Value == value);
            if (match != null) return match;

            match = new TimeOption(value);
            _times.Insert(_times.FindIndex(o => o.Value > value) is var i and >= 0 ? i : _times.Count, match);
            CmbShiftStart.Items.Refresh();
            CmbLateCutoff.Items.Refresh();
            CmbAbsentCutoff.Items.Refresh();
            return match;
        }

        private string? ReadShift(out StaffShift? shift)
        {
            // Non-admins can't change it, so keep whatever is saved.
            shift = SupabaseService.Instance.IsAdmin ? null : _original?.Shift;
            if (!SupabaseService.Instance.IsAdmin || CmbShiftMode.SelectedIndex != 1) return null;

            if (CmbShiftStart.SelectedItem is not TimeOption start ||
                CmbLateCutoff.SelectedItem is not TimeOption late ||
                CmbAbsentCutoff.SelectedItem is not TimeOption absent)
                return "Pick all three shift times, or switch back to the default shift.";
            if (late.Value < start.Value) return "\"Late after\" can't be earlier than the shift start.";
            if (absent.Value <= late.Value) return "\"Absent after\" must be later than \"Late after\".";

            shift = new StaffShift(start.Value, late.Value, absent.Value);
            return null;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            string firstName = TxtFirstName.Text.Trim();
            string lastName = TxtLastName.Text.Trim();
            string email = TxtEmail.Text.Trim();
            string uid = TxtRfidUid.Text.Trim().ToUpper();
            var department = CmbDepartment.SelectedItem as string;
            var program = CmbProgram.SelectedItem as string;
            var position = CmbPosition.SelectedItem as string;

            var missing = new List<string>();
            if (firstName.Length == 0) missing.Add("first name");
            if (lastName.Length == 0) missing.Add("last name");
            if (email.Length == 0) missing.Add("email");
            if (uid.Length == 0) missing.Add("RFID UID");
            if (department is null) missing.Add("department");
            if (program is null) missing.Add("program");
            if (position is null) missing.Add("position");

            if (missing.Count > 0)
            {
                StatusText.Text = $"Please fill in: {string.Join(", ", missing)}.";
                return;
            }

            if (ReadShift(out var shift) is { } shiftError)
            {
                StatusText.Text = shiftError;
                return;
            }

            var holder = _existing.FirstOrDefault(s =>
                s.Id != _original?.Id && string.Equals(s.RfidUid, uid, StringComparison.OrdinalIgnoreCase));
            if (holder != null)
            {
                StatusText.Text = "";
                EnrollmentView.ShowRfidTaken(this, uid, holder.FullName);
                TxtRfidUid.Focus();
                TxtRfidUid.SelectAll();
                return;
            }

            BtnSave.IsEnabled = false;
            StatusText.Text = "";

            try
            {
                Updated = _original is null
                    ? await SupabaseService.Instance.AddStaffAsync(
                        firstName, TxtMiddleName.Text.Trim(), lastName, email,
                        department!, program!, position!, uid, shift)
                    : await SupabaseService.Instance.UpdateStaffAsync(
                        _original.Id, firstName, TxtMiddleName.Text.Trim(), lastName, email,
                        department!, program!, position!, uid, shift);
                DialogResult = true;
            }
            catch (Exception ex) when (SupabaseService.IsDuplicateRfid(ex))
            {
                StatusText.Text = "";
                EnrollmentView.ShowRfidTaken(this, uid, null);
                TxtRfidUid.Focus();
                TxtRfidUid.SelectAll();
                BtnSave.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Couldn't save: {ex.Message}";
                BtnSave.IsEnabled = true;
            }
        }

        private static TimeSpan ParseTime(string value) =>
            TimeSpan.TryParse(value, out var t) ? t : TimeSpan.Zero;

        private static string Fmt(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt");
    }
}
