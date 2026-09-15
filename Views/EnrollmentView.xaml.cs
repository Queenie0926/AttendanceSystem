using Attendance_System.Models;
using Attendance_System.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
                SetStatus("Loading staff…", StatusKind.Info);
                var staff = await SupabaseService.Instance.GetStaffAsync();

                _staff.Clear();
                foreach (var s in staff)
                    _staff.Add(s);

                SetStatus("", StatusKind.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't load staff: {ex.Message}", StatusKind.Error);
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
                SetStatus($"Please fill in: {string.Join(", ", missing)}.", StatusKind.Error);
                return;
            }

            BtnEnroll.IsEnabled = false;
            SetStatus("Enrolling…", StatusKind.Info);

            try
            {
                var added = await SupabaseService.Instance.AddStaffAsync(
                    firstName, middleName, lastName, email, department!, program!, position!, uid);
                _staff.Add(added);

                ClearForm();
                SetStatus($"{added.FullName} was enrolled.", StatusKind.Success);
            }
            catch (Exception ex)
            {
                SetStatus($"Enrollment failed: {ex.Message}", StatusKind.Error);
            }
            finally
            {
                BtnEnroll.IsEnabled = true;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            SetStatus("", StatusKind.Info);
        }

        private void ClearForm()
        {
            TxtFirstName.Clear();
            TxtMiddleName.Clear();
            TxtLastName.Clear();
            TxtEmail.Clear();
            TxtRfidUid.Clear();
            CmbProgram.SelectedIndex = -1;
            CmbPosition.SelectedIndex = -1;
            TxtFirstName.Focus();
        }

        // ── Row actions ──────────────────────────────────────────────────

        private void RowActions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: StaffRecord staff } button) return;

            var view = new MenuItem { Header = "View details" };
            view.Click += (s, args) => ShowDetails(staff);

            var edit = new MenuItem { Header = "Edit staff" };
            edit.Click += (s, args) => EditStaff(staff);

            var remove = new MenuItem { Header = "Remove staff…", Style = (Style)FindResource("DangerMenuItemStyle") };
            remove.Click += async (s, args) => await RemoveStaffAsync(staff);

            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            };
            menu.Items.Add(view);
            menu.Items.Add(edit);
            menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparatorStyle") });
            menu.Items.Add(remove);
            menu.IsOpen = true;
        }

        private void StaffGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Only rows count — ignore double-clicks on the header or empty space.
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(StaffGrid, source) is DataGridRow { Item: StaffRecord staff })
                ShowDetails(staff);
        }

        private void ShowDetails(StaffRecord staff)
        {
            new StaffDetailsWindow(staff) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void EditStaff(StaffRecord staff)
        {
            var dialog = new StaffEditWindow(staff) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true || dialog.Updated is null) return;

            // Replace in place so the row keeps its position in the table.
            int index = _staff.IndexOf(staff);
            if (index >= 0) _staff[index] = dialog.Updated;
            SetStatus($"{dialog.Updated.FullName} was updated.", StatusKind.Success);
        }

        private async Task RemoveStaffAsync(StaffRecord staff)
        {
            var confirm = MessageBox.Show(Window.GetWindow(this),
                $"Remove {staff.FullName}?\n\n" +
                "This permanently deletes their enrollment and all of their attendance records. " +
                "Their RFID card will stop working. This can't be undone.",
                "Remove staff", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await SupabaseService.Instance.DeleteStaffAsync(staff.Id);
                _staff.Remove(staff);
                SetStatus($"{staff.FullName} was removed.", StatusKind.Success);
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't remove {staff.FullName}: {ex.Message}", StatusKind.Error);
            }
        }

        private enum StatusKind { Info, Success, Error }

        private void SetStatus(string message, StatusKind kind)
        {
            StatusText.Text = message;
            StatusText.Foreground = (Brush)FindResource(kind switch
            {
                StatusKind.Success => "SuccessBrush",
                StatusKind.Error => "DangerBrush",
                _ => "TextSecondaryBrush",
            });
        }
    }
}
