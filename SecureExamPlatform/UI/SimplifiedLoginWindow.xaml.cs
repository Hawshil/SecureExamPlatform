using System;
using System.Windows;
using System.Windows.Input;
using SecureExamPlatform.Core;

namespace SecureExamPlatform.UI
{
    public partial class SimplifiedLoginWindow : Window
    {
        private readonly SimpleCredentialManager _credManager;

        public SimplifiedLoginWindow()
        {
            InitializeComponent();
            _credManager = new SimpleCredentialManager();

            // Set fullscreen
            this.WindowState = WindowState.Maximized;
            this.WindowStyle = WindowStyle.None;
            this.Topmost = true;

            // Add keyboard handlers
            this.PreviewKeyDown += Window_PreviewKeyDown;

            // Focus on first textbox
            this.Loaded += (s, e) => StudentIdBox.Focus();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Emergency exit: Ctrl+Shift+Esc
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Escape)
            {
                var result = MessageBox.Show(
                    "Exit the application?\n\nThis will close the login window.",
                    "Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    Application.Current.Shutdown();
                }
                e.Handled = true;
            }

            // Allow Enter key to submit
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
            }

            // Block Alt+F4, Windows key, etc.
            if (Keyboard.Modifiers == ModifierKeys.Alt && (e.Key == Key.F4 || e.Key == Key.Tab))
            {
                e.Handled = true;
            }
            if (e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = StudentIdBox.Text.Trim();
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
                var (success, message, examId) = _credManager.ValidateLogin(studentId, code);

                if (!success)
                {
                    StatusText.Text = $"❌ {message}";
                    LoginButton.IsEnabled = true;
                    LoginCodeBox.Clear();
                    LoginCodeBox.Focus();
                    return;
                }

                StatusText.Text = "✓ Login successful! Launching exam...";
                await System.Threading.Tasks.Task.Delay(500); // Brief pause

                // Launch exam
                var examWindow = new ExamWindow();
                bool started = await examWindow.StartExam(studentId, code, examId);

                if (started)
                {
                    examWindow.Show();
                    this.Close();
                }
                else
                {
                    StatusText.Text = "❌ Failed to start exam. Contact your instructor.";
                    LoginButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
                LoginButton.IsEnabled = true;
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Exit the application?",
                "Exit",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }
    }
}