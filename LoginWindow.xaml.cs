using System.Windows;
using System.Windows.Input;
using Attendance_System.Services;
using Supabase.Gotrue.Exceptions;

namespace Attendance_System
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            await AttemptLoginAsync();
        }

        private async void TxtPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await AttemptLoginAsync();
        }

        private async Task AttemptLoginAsync()
        {
            string email = TxtEmail.Text.Trim();
            string password = TxtPassword.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                StatusText.Text = "Email and password are required.";
                MessageBox.Show(this, "Email and password are required.", "Sign-In Failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnLogin.IsEnabled = false;
            StatusText.Text = "Signing in...";
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                var session = await SupabaseService.Instance.SignInAsync(email, password);
                if (session?.AccessToken is null)
                {
                    StatusText.Foreground = System.Windows.Media.Brushes.Red;
                    StatusText.Text = "Invalid email or password.";
                    MessageBox.Show(this, "Invalid email or password.", "Sign-In Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                DialogResult = true;
                Close();
            }
            catch (GotrueException)
            {
                // Bad credentials, unconfirmed user, etc. — Supabase Auth rejected the sign-in.
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                StatusText.Text = "Invalid email or password.";
                MessageBox.Show(this, "Invalid email or password.", "Sign-In Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                // Network/config problems shouldn't be reported as "wrong password".
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                StatusText.Text = $"Sign-in failed: {ex.Message}";
                MessageBox.Show(this, $"Could not sign in:\n\n{ex.Message}", "Sign-In Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnLogin.IsEnabled = true;
            }
        }
    }
}
