using SecureExamPlatform.Core;
using System;
using System.ComponentModel;
using System.Management;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SecureExamPlatform.UI
{
    /// <summary>
    /// Student login window for exam authentication
    /// </summary>
    public partial class LoginWindow : Window, INotifyPropertyChanged
    {
        private readonly ExamSessionManager _sessionManager;
        private string _computerName;
        private string _hardwareId;
        private bool _isAuthenticating = false;

        public LoginWindow()
        {
            InitializeComponent();
            DataContext = this;

            _sessionManager = new ExamSessionManager();

            // Get computer information
            InitializeComputerInfo();

            // Set initial focus
            Loaded += (s, e) => StudentIdTextBox.Focus();
        }

        private void InitializeComputerInfo()
        {
            try
            {
                // Get computer name
                ComputerName = Environment.MachineName;

                // Generate hardware ID (simplified version for free tools)
                HardwareId = GenerateHardwareId();
            }
            catch (Exception ex)
            {
                ComputerName = "UNKNOWN";
                HardwareId = "ERROR";
                LogError($"Failed to get computer info: {ex.Message}");
            }
        }

        private string GenerateHardwareId()
        {
            var components = new StringBuilder();

            try
            {
                // Get processor ID
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        components.Append(obj["ProcessorId"]?.ToString() ?? "UNKNOWN");
                        break;
                    }
                }

                // Get motherboard serial
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        components.Append("|");
                        components.Append(obj["SerialNumber"]?.ToString() ?? "UNKNOWN");
                        break;
                    }
                }

                // Get disk serial
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        components.Append("|");
                        components.Append(obj["SerialNumber"]?.ToString() ?? "UNKNOWN");
                        break;
                    }
                }
            }
            catch
            {
                components.Append($"FALLBACK|{Environment.MachineName}|{Environment.UserName}");
            }

            // Create hash of components
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(components.ToString()));
                // Return first 8 bytes as hex for display
                return BitConverter.ToString(hashBytes, 0, 8).Replace("-", "");
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isAuthenticating) return;

            _isAuthenticating = true;
            ShowLoading(true);
            HideError();

            try
            {
                string studentId = StudentIdTextBox.Text.Trim();
                string sessionToken = SessionTokenBox.Password.Trim();

                // Basic validation
                if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(sessionToken))
                {
                    ShowError("Please enter both Student ID and Session Token");
                    return;
                }

                // Validate format
                if (!ValidateCredentials(studentId, sessionToken))
                {
                    ShowError("Invalid credential format. Please check your input.");
                    return;
                }

                // Create session with hardware binding
                string examId = ExtractExamIdFromToken(sessionToken);
                var result = await _sessionManager.StartSession(studentId, sessionToken, examId);

                if (!result.Success)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                // Launch exam window
                var examWindow = new ExamWindow();
                bool examStarted = await examWindow.StartExam(studentId, sessionToken, examId);

                if (examStarted)
                {
                    // Hide login window and show exam
                    this.Hide();
                    examWindow.ShowDialog();

                    // After exam window closes, close application
                    System.Windows.Application.Current.Shutdown();
                }
                else
                {
                    ShowError("Failed to start exam. Please contact support.");
                    _sessionManager.EndSession(false);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Login failed: {ex.Message}");
                LogError(ex.ToString());
            }
            finally
            {
                _isAuthenticating = false;
                ShowLoading(false);
            }
        }

        private bool ValidateCredentials(string studentId, string token)
        {
            // Student ID format: Alphanumeric, 5-20 characters
            if (studentId.Length < 5 || studentId.Length > 20)
                return false;

            foreach (char c in studentId)
            {
                if (!char.IsLetterOrDigit(c))
                    return false;
            }

            // Token format: At least 10 characters, contains letters and numbers
            if (token.Length < 10)
                return false;

            bool hasLetter = false;
            bool hasDigit = false;

            foreach (char c in token)
            {
                if (char.IsLetter(c)) hasLetter = true;
                if (char.IsDigit(c)) hasDigit = true;
            }

            return hasLetter && hasDigit;
        }

        private string ExtractExamIdFromToken(string token)
        {
            // Simple extraction - in production, this would be more sophisticated
            // Token format example: EXAM2025-ABC123-XYZ789
            if (token.Contains("-"))
            {
                string[] parts = token.Split('-');
                if (parts.Length > 0 && parts[0].StartsWith("EXAM"))
                {
                    return parts[0];
                }
            }

            // Fallback: use hash of token
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(token));
                return "EXAM" + BitConverter.ToString(hash, 0, 4).Replace("-", "");
            }
        }

        private void ShowError(string message)
        {
            ErrorMessage.Text = message;
            ErrorMessage.Visibility = Visibility.Visible;

            // Animate error message
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            ErrorMessage.BeginAnimation(OpacityProperty, animation);
        }

        private void HideError()
        {
            ErrorMessage.Visibility = Visibility.Collapsed;
        }

        private void ShowLoading(bool show)
        {
            LoadingIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            LoginButton.IsEnabled = !show;
            StudentIdTextBox.IsEnabled = !show;
            SessionTokenBox.IsEnabled = !show;

            if (show)
            {
                LoginButton.Content = "Authenticating...";
            }
            else
            {
                LoginButton.Content = "Start Exam";
            }
        }

        private void SessionTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ValidateForm();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85));
            }
        }

        private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                passwordBox.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
            }
        }

        private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                passwordBox.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85));
            }
        }

        private void ValidateForm()
        {
            bool isValid = !string.IsNullOrWhiteSpace(StudentIdTextBox.Text) &&
                          !string.IsNullOrWhiteSpace(SessionTokenBox.Password) &&
                          StudentIdTextBox.Text.Length >= 5 &&
                          SessionTokenBox.Password.Length >= 10;

            LoginButton.IsEnabled = isValid && !_isAuthenticating;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to exit? You will need to re-enter your credentials to start the exam.",
                "Confirm Exit",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void LogError(string message)
        {
            try
            {
                string logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    $"login_errors_{DateTime.Now:yyyyMMdd}.log"
                );

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Silently fail if logging doesn't work
            }
        }

        // Properties for data binding
        public string ComputerName
        {
            get => _computerName;
            set
            {
                _computerName = value;
                OnPropertyChanged();
            }
        }

        public string HardwareId
        {
            get => _hardwareId;
            set
            {
                _hardwareId = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}