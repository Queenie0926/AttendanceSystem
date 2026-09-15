using Attendance_System.Models;
using Attendance_System.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Attendance_System.Views
{
    public partial class EnrollmentView : UserControl
    {
        private readonly ObservableCollection<StaffRecord> _staff = new();

        public EnrollmentView()
        {
            InitializeComponent();
            StaffGrid.ItemsSource = _staff;

            CmbDepartment.ItemsSource = StaffOptions.Departments;
            CmbDepartment.SelectedIndex = 0; // only one department in this prototype

            CmbPosition.ItemsSource = StaffOptions.Positions;

            Loaded += async (s, e) => await LoadStaffAsync();
        }

        private void CmbDepartment_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var department = CmbDepartment.SelectedItem as string;
            if (department != null && StaffOptions.ProgramsByDepartment.TryGetValue(department, out var programs))
            {
                CmbProgram.ItemsSource = programs;
                CmbProgram.SelectedIndex = -1;
            }
        }

        private async Task LoadStaffAsync()
        {
            try
            {
                StatusText.Text = "Loading staff...";
                var staff = await SupabaseService.Instance.GetStaffAsync();

                _staff.Clear();
                foreach (var s in staff)
                    _staff.Add(s);

                StatusText.Text = "";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load staff: {ex.Message}";
            }
        }

        private async void EnrollButton_Click(object sender, RoutedEventArgs e)
        {
            string firstName = TxtFirstName.Text.Trim();
            string middleName = TxtMiddleName.Text.Trim();
            string lastName = TxtLastName.Text.Trim();
            string email = TxtEmail.Text.Trim();
            string uid = TxtRfidUid.Text.Trim();
            string? department = CmbDepartment.SelectedItem as string;
            string? program = CmbProgram.SelectedItem as string;
            string? position = CmbPosition.SelectedItem as string;

            if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName) ||
                string.IsNullOrEmpty(email) || string.IsNullOrEmpty(uid))
            {
                StatusText.Text = "First name, last name, email, and RFID UID are required.";
                return;
            }

            if (department is null || program is null || position is null)
            {
                StatusText.Text = "Department, program, and position are required.";
                return;
            }

            try
            {
                var added = await SupabaseService.Instance.AddStaffAsync(
                    firstName, middleName, lastName, email, department, program, position, uid);
                _staff.Add(added);

                StatusText.Text = $"{added.FullName} enrolled successfully.";
                TxtFirstName.Clear();
                TxtMiddleName.Clear();
                TxtLastName.Clear();
                TxtEmail.Clear();
                TxtRfidUid.Clear();
                CmbProgram.SelectedIndex = -1;
                CmbPosition.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Enrollment failed: {ex.Message}";
            }
        }
    }
}
