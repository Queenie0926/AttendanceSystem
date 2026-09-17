using Attendance_System.Models;
using Attendance_System.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Attendance_System.Views
{
    public partial class EnrollmentView : UserControl
    {
        private const string AllPrograms = "All programs";

        private readonly ObservableCollection<StaffRecord> _staff = new();
        private readonly ICollectionView _view;

        public EnrollmentView()
        {
            InitializeComponent();

            _view = CollectionViewSource.GetDefaultView(_staff);
            _view.Filter = MatchesFilters;
            StaffGrid.ItemsSource = _view;

            CmbProgramFilter.ItemsSource = new[] { AllPrograms }
                .Concat(StaffOptions.ProgramsByDepartment.Values.SelectMany(p => p)).ToList();
            CmbProgramFilter.SelectedIndex = 0;

            _staff.CollectionChanged += (s, e) => UpdateCount();
            Loaded += async (s, e) => await LoadStaffAsync();
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

        // ── Search & filter ──────────────────────────────────────────────

        private bool MatchesFilters(object item)
        {
            if (item is not StaffRecord s) return false;

            if (CmbProgramFilter.SelectedItem is string program && program != AllPrograms && s.Program != program)
                return false;

            string q = TxtSearch.Text.Trim();
            return q.Length == 0
                || s.FullName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Email.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.RfidUid.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

        private void ProgramFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFilter();

        private void RefreshFilter()
        {
            if (_view is null) return;   // fires during InitializeComponent
            _view.Refresh();
            UpdateCount();
        }

        private void UpdateCount()
        {
            int shown = _view.Cast<object>().Count();
            TxtCount.Text = shown == _staff.Count ? $"{_staff.Count}" : $"{shown} of {_staff.Count}";

            bool hasStaff = _staff.Count > 0;
            TxtEmptyTitle.Text = hasStaff ? "No matching staff" : "No staff enrolled yet";
            TxtEmptyHelp.Text = hasStaff
                ? "Try a different search or program."
                : "Select Enroll staff to register someone and link their RFID card.";
        }

        // ── Enroll / edit / remove ───────────────────────────────────────

        private void EnrollButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new StaffEditWindow(null, _staff) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true || dialog.Updated is null) return;

            _staff.Add(dialog.Updated);
            StaffGrid.SelectedItem = dialog.Updated;
            StaffGrid.ScrollIntoView(dialog.Updated);
            SetStatus($"{dialog.Updated.FullName} was enrolled.", StatusKind.Success);
        }

        internal static void ShowRfidTaken(Window? owner, string uid, string? holderName)
        {
            string who = holderName ?? "another staff member";
            string text = $"RFID UID {uid} is already assigned to {who}.\n\n" +
                          "Each card can only belong to one person. Scan a different card, or edit or remove " +
                          $"{holderName ?? "that staff member"} first.";
            if (owner is null)
                MessageBox.Show(text, "RFID card already in use", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(owner, text, "RFID card already in use", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void EditRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: StaffRecord staff }) EditStaff(staff);
        }

        private async void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: StaffRecord staff }) await RemoveStaffAsync(staff);
        }

        private void StaffGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Only rows count — ignore double-clicks on the header, empty space or the action buttons.
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(StaffGrid, source) is DataGridRow { Item: StaffRecord staff })
                ShowDetails(staff);
        }

        private void StaffGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && StaffGrid.SelectedItem is StaffRecord staff)
            {
                ShowDetails(staff);
                e.Handled = true;
            }
        }

        private void ShowDetails(StaffRecord staff)
        {
            new StaffDetailsWindow(staff) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void EditStaff(StaffRecord staff)
        {
            var dialog = new StaffEditWindow(staff, _staff) { Owner = Window.GetWindow(this) };
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
            StatusText.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Foreground = (Brush)FindResource(kind switch
            {
                StatusKind.Success => "SuccessBrush",
                StatusKind.Error => "DangerBrush",
                _ => "TextSecondaryBrush",
            });
        }
    }
}
