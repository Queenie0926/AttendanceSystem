using Attendance_System.Data.Entities;
using Attendance_System.Models;
using Attendance_System.Services;
using System.Windows;
using System.Windows.Controls;

namespace Attendance_System.Views
{
    /// <summary>Admin-only: edit names and roles in the profiles table.</summary>
    public partial class UsersView : UserControl
    {
        public string[] RoleOptions => UserRoles.All;

        public UsersView()
        {
            InitializeComponent();
            Loaded += async (s, e) => await LoadUsersAsync();
        }

        private async Task LoadUsersAsync()
        {
            BtnRefresh.IsEnabled = false;
            LoadingText.Visibility = Visibility.Visible;
            try
            {
                UsersGrid.ItemsSource = await SupabaseService.Instance.GetProfilesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't load users:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRefresh.IsEnabled = true;
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadUsersAsync();

        private async void SaveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: UserProfileRecord user } button) return;

            button.IsEnabled = false;
            try
            {
                await SupabaseService.Instance.UpdateProfileAsync(user.Id, user.FullName, user.Role);
                user.MarkSaved();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't save {user.Email}:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                button.IsEnabled = user.IsDirty;
            }
        }
    }
}
