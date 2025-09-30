using SecureExamPlatform.UI;
using System.Windows;

namespace SecureExamPlatform
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create and show the main exam window.
            var examWindow = new ExamWindow();
            examWindow.Show();

            // Automatically start a test session with mock credentials.
            bool examStarted = await examWindow.StartExam("S12345", "mock-session-token", "CS101-FINAL");

            // If the session fails to start, close the application.
            if (!examStarted)
            {
                System.Windows.MessageBox.Show("Failed to start the exam session.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                examWindow.Close();
                Shutdown();
            }
        }
    }
}