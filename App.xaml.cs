using System.Windows;
using Attendance_System.Config;
using Attendance_System.Services;

namespace Attendance_System
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                AppConfig.Load();
                await SupabaseService.Instance.InitializeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not connect to the backend:\n\n{ex.Message}",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            ShowLogin();
        }

        // ShutdownMode is OnExplicitShutdown (App.xaml), so closing a window
        // never ends the app by itself — this method decides what comes next.
        private void ShowLogin()
        {
            var login = new LoginWindow();
            if (login.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            var main = new MainWindow();
            MainWindow = main;
            main.Closed += (s, args) =>
            {
                if (main.SignedOut)
                    ShowLogin();
                else
                    Shutdown();
            };
            main.Show();
        }
    }
}
