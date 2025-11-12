using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace SecureExam.Student
{
    public partial class App : Application
    {
        private Mutex _appMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level =
                System.Diagnostics.SourceLevels.Critical;

            bool createdNew;
            _appMutex = new Mutex(true, "SecureExamStudent_SingleInstance", out createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "Student exam application is already running!",
                    "Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Current.Shutdown();
                return;
            }

            InitializeDirectories();
        }

        private void InitializeDirectories()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SecureExam"
            );

            string[] directories = {
                Path.Combine(appData, "Exams"),
                Path.Combine(appData, "Submissions"),
                Path.Combine(appData, "Student", "Sessions"),
                Path.Combine(appData, "Student", "Logs"),
                Path.Combine(appData, "Security", "Logs")
            };

            foreach (var dir in directories)
            {
                Directory.CreateDirectory(dir);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _appMutex?.ReleaseMutex();
            _appMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
