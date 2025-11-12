using System;
using System.IO;
using System.Windows;

namespace SecureExam.Admin
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level =
                System.Diagnostics.SourceLevels.Critical;

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
                Path.Combine(appData, "Credentials"),
                Path.Combine(appData, "Grades"),
                Path.Combine(appData, "Logs")
            };

            foreach (var dir in directories)
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
