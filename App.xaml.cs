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

            var login = new LoginWindow();
            bool? signedIn = login.ShowDialog();

            if (signedIn == true)
            {
                var main = new MainWindow();
                MainWindow = main;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                main.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }
}
