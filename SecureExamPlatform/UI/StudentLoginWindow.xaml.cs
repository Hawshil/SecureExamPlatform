using System;
using System.Windows;
using System.Windows.Input;
using SecureExamPlatform.Core;

namespace SecureExamPlatform.UI
{
    public partial class StudentLoginWindow : Window
    {
        private readonly LabCredentialManager _credManager;
        private const string EXIT_PASSWORD = "EXIT2025"; // Change this

        public StudentLoginWindow()
        {
            InitializeComponent();
            _credManager = new LabCredentialManager();

            // Set fullscreen
            this.WindowState = WindowState.Maximized;
            this.WindowStyle = WindowStyle.None;
            this.Topmost = true;
            this.ResizeMode = ResizeMode.NoResize;

            // Add keyboard handlers
            this.PreviewKeyDown += Window_PreviewKeyDown;

            // Focus on first textbox
            this.Loaded += (s, e) => StudentIdBox.Focus();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Alt+E for Exit (with password)
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.E)
            {
                PromptExitWithPassword();
                e.Handled = true;
                return;
            }

            // Allow Enter key to navigate/submit
            if (e.Key == Key.Enter)
            {
                if (StudentIdBox.IsFocused)
                {
                    LoginCodeBox.Focus();
                }
                else if (LoginCodeBox.IsFocused)
                {
                    LoginButton_Click(sender, e);
                }
                e.Handled = true;
                return;
            }

            // Block most keyboard shortcuts
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                if (e.Key == Key.F4 || e.Key == Key.Tab)
                {
                    e.Handled = true;
                }
            }

            // Block Windows key
            if (e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
            }

            // Block Ctrl combinations except Ctrl+C/V for textboxes
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key != Key.C && e.Key != Key.V && e.Key != Key.X)
                {
                    e.Handled = true;
                }
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = StudentIdBox.Text.Trim().ToUpper();
            string code = LoginCodeBox.Text.Trim();

            if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(code))
            {
                StatusText.Text = "❌ Please enter both Student ID and Login Code";
                return;
            }

            // Disable button while processing
            LoginButton.IsEnabled = false;
            StatusText.Text = "⏳ Validating credentials...";

            try
            {
                var validationResult = _credManager.ValidateStudentLogin(studentId, code);

                if (!validationResult.Success)
                {
                    StatusText.Text = $"❌ {validationResult.Message}";
                    LoginButton.IsEnabled = true;
                    LoginCodeBox.Clear();
                    LoginCodeBox.Focus();
                    return;
                }

                StatusText.Text = "✓ Credentials valid! Checking for existing sessions...";
                await System.Threading.Tasks.Task.Delay(300);

                // Check for and terminate any existing sessions
                bool sessionTerminated = await _credManager.TerminatePreviousSession(studentId);
                if (sessionTerminated)
                {
                    StatusText.Text = "✓ Previous session terminated. Starting exam...";
                    await System.Threading.Tasks.Task.Delay(500);
                }

                // Mark credential as used (one-time use)
                _credManager.MarkCredentialAsUsed(studentId, code);

                StatusText.Text = "✓ Launching secure exam window...";
                await System.Threading.Tasks.Task.Delay(300);

                // Launch exam
                var examWindow = new SecureExamWindow();
                bool started = await examWindow.InitializeExam(
                    studentId,
                    code,
                    validationResult.ExamId,
                    validationResult.LabId
                );

                if (started)
                {
                    examWindow.Show();
                    this.Close();
                }
                else
                {
                    StatusText.Text = "❌ Failed to start exam. Contact your invigilator.";
                    LoginButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
                LoginButton.IsEnabled = true;
                LogError($"Login error: {ex}");
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            PromptExitWithPassword();
        }

        private void PromptExitWithPassword()
        {
            var passwordDialog = new ExitPasswordDialog();
            if (passwordDialog.ShowDialog() == true)
            {
                if (passwordDialog.EnteredPassword == EXIT_PASSWORD)
                {
                    LogEvent("Application exited via password");
                    Application.Current.Shutdown();
                }
                else
                {
                    MessageBox.Show(
                        "Incorrect exit password. Contact your invigilator if you need to exit.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private void LogEvent(string message)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Student", "Logs", $"login_{DateTime.Now:yyyyMMdd}.log"
                );
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        private void LogError(string message)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Student", "Logs", $"errors_{DateTime.Now:yyyyMMdd}.log"
                );
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }
    }
}
