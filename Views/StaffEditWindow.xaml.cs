using Attendance_System.Models;
using Attendance_System.Services;
using System.Windows;
using System.Windows.Controls;

namespace Attendance_System.Views
{
    public partial class StaffEditWindow : Window
    {
        private readonly StaffRecord _original;

        /// <summary>The saved record, set when the dialog closes with DialogResult = true.</summary>
        public StaffRecord? Updated { get; private set; }

        public StaffEditWindow(StaffRecord staff)
        {
            InitializeComponent();
            _original = staff;

            TxtSubtitle.Text = $"Changes apply to {staff.FullName}'s next tap.";
            TxtFirstName.Text = staff.FirstName;
            TxtMiddleName.Text = staff.MiddleName ?? "";
            TxtLastName.Text = staff.LastName;
            TxtEmail.Text = staff.Email;
            TxtRfidUid.Text = staff.RfidUid;

            CmbDepartment.ItemsSource = StaffOptions.Departments;
            CmbDepartment.SelectedItem = staff.Department;   // fills CmbProgram via SelectionChanged
            CmbProgram.SelectedItem = staff.Program;
            CmbPosition.ItemsSource = StaffOptions.Positions;
            CmbPosition.SelectedItem = staff.PositionRole;

            Loaded += (s, e) => TxtFirstName.Focus();
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

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            string firstName = TxtFirstName.Text.Trim();
            string lastName = TxtLastName.Text.Trim();
            string email = TxtEmail.Text.Trim();
            string uid = TxtRfidUid.Text.Trim();
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

            BtnSave.IsEnabled = false;
            StatusText.Text = "";

            try
            {
                Updated = await SupabaseService.Instance.UpdateStaffAsync(
                    _original.Id, firstName, TxtMiddleName.Text.Trim(), lastName, email,
                    department!, program!, position!, uid);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Couldn't save: {ex.Message}";
                BtnSave.IsEnabled = true;
            }
        }
    }
}
