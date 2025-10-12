using System.Windows;

namespace SecureExamPlatform
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Set up global exception handling
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            
            // Check if running with sufficient privileges
            CheckSystemRequirements();
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\nThe application will continue running.", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            MessageBox.Show($"A critical error occurred: {exception?.Message}\n\nThe application will now close.", 
                "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void CheckSystemRequirements()
        {
            try
            {
                // Check Windows version
                var version = Environment.OSVersion.Version;
                if (version.Major < 6 || (version.Major == 6 && version.Minor < 1))
                {
                    MessageBox.Show("This application requires Windows 7 or later.", 
                        "System Requirements", MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.Exit(1);
                }

                // Check .NET Framework version (handled by target framework)
                
                // Create necessary directories
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "SecureExam");
                    
                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                    Directory.CreateDirectory(Path.Combine(appDataPath, "Submissions"));
                    Directory.CreateDirectory(Path.Combine(appDataPath, "Logs"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize application: {ex.Message}", 
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }
    }
}
