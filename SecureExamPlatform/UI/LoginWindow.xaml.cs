using SecureExamPlatform.Core;
using SecureExamPlatform.Security;
using System;
using System.ComponentModel;
using System.Management;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SecureExamPlatform.UI
{
    public partial class LoginWindow : Window, INotifyPropertyChanged
    {
        private readonly CredentialManager credentialManager;
        private string computerName = "";
        private string hardwareId = "";
        private bool isAuthenticating;
        private DispatcherTimer totpTimer;
        private StudentCredential pendingCredential;

        public string ComputerName
        {
            get => computerName;
            set { computerName = value; OnPropertyChanged(); }
        }

        public string HardwareId
        {
            get => hardwareId;
            set { hardwareId = value; OnPropertyChanged(); }
        }

        public LoginWindow()
        {
            InitializeComponent();
            DataContext = this;
            credentialManager = new CredentialManager();
            totpTimer = new DispatcherTimer();

            // Initialize asynchronously to avoid blocking UI
            Loaded += async (s, e) => await InitializeAsync();

            StudentIdTextBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) AccessTokenBox.Focus(); };
            AccessTokenBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) HandleEnterKey(); };
            TotpCodeTextBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) LoginButton_Click(s, e); };
        }

        private async Task InitializeAsync()
        {
            ShowLoading("Initializing...");

            // Run hardware detection in background
            await Task.Run(() =>
            {
                try
                {
                    ComputerName = Environment.MachineName;
                    HardwareId = GenerateHardwareId();
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowStatus($"Warning: {ex.Message}", false));
                }
            });

            HideLoading();
            StudentIdTextBox.Focus();
            SetupTotpTimer();
        }

        private string GenerateHardwareId()
        {
            try
            {
                StringBuilder fingerprint = new StringBuilder();

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystemProduct"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        fingerprint.Append(obj["UUID"]?.ToString() ?? "");
                        break; // Only need first result
                    }
                }

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        fingerprint.Append(obj["SerialNumber"]?.ToString() ?? "");
                        break; // Only need first result
                    }
                }

                using (var sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(fingerprint.ToString()));
                    return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
                }
            }
            catch
            {
                return "UNKNOWN-HARDWARE";
            }
        }

        private void SetupTotpTimer()
        {
            totpTimer.Interval = TimeSpan.FromSeconds(1);
            totpTimer.Tick += (s, e) =>
            {
                int remaining = TotpManager.GetRemainingSeconds();
                TotpTimerText.Text = $"Code expires in {remaining}s";

                if (remaining <= 5)
                {
                    TotpTimerText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
                }
                else
                {
                    TotpTimerText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
                }
            };
        }

        private void HandleEnterKey()
        {
            if (TotpPanel.Visibility == Visibility.Visible)
            {
                TotpCodeTextBox.Focus();
            }
            else
            {
                LoginButton_Click(this, new RoutedEventArgs());
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (isAuthenticating) return;

            string studentId = StudentIdTextBox.Text.Trim();
            string accessToken = AccessTokenBox.Password.Trim();
            string totpCode = TotpCodeTextBox.Text.Trim();

            // Validation
            if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(accessToken))
            {
                ShowStatus("Please enter Student ID and Access Token", true);
                return;
            }

            if (TotpPanel.Visibility == Visibility.Visible && string.IsNullOrEmpty(totpCode))
            {
                ShowStatus("Please enter the 6-digit authentication code", true);
                return;
            }

            isAuthenticating = true;
            DisableInputs();

            try
            {
                if (TotpPanel.Visibility == Visibility.Collapsed)
                {
                    // First stage: Validate credentials
                    ShowLoading("Validating credentials...");

                    var cred = await Task.Run(() => credentialManager.GetAllCredentials().Find(c =>
                        c.StudentId == studentId &&
                        c.AccessToken == accessToken &&
                        !c.IsUsed));

                    if (cred == null)
                    {
                        ShowStatus("Invalid credentials. Please check your Student ID and Access Token.", true);
                        isAuthenticating = false;
                        EnableInputs();
                        HideLoading();
                        return;
                    }

                    // Check expiration
                    if (DateTime.UtcNow > cred.ExpiresAt)
                    {
                        ShowStatus("These credentials have expired. Please contact your instructor.", true);
                        isAuthenticating = false;
                        EnableInputs();
                        HideLoading();
                        return;
                    }

                    // Check hardware match
                    if (cred.HardwareId != HardwareId)
                    {
                        ShowStatus($"Hardware mismatch. These credentials are for: {cred.ComputerName}", true);
                        isAuthenticating = false;
                        EnableInputs();
                        HideLoading();
                        return;
                    }

                    // Credentials valid, show TOTP panel
                    pendingCredential = cred;
                    TotpPanel.Visibility = Visibility.Visible;
                    totpTimer.Start();
                    isAuthenticating = false;
                    EnableInputs();
                    HideLoading();
                    TotpCodeTextBox.Focus();
                    ShowStatus("Enter the 6-digit code from your authenticator app", false);
                }
                else
                {
                    // Second stage: Validate TOTP and launch exam
                    ShowLoading("Verifying authentication code...");

                    if (pendingCredential == null)
                    {
                        ShowStatus("Session expired. Please start over.", true);
                        TotpPanel.Visibility = Visibility.Collapsed;
                        isAuthenticating = false;
                        EnableInputs();
                        HideLoading();
                        return;
                    }

                    // Validate TOTP
                    bool isValidTotp = await Task.Run(() =>
                        TotpManager.ValidateCode(pendingCredential.TotpSecret, totpCode));

                    if (!isValidTotp)
                    {
                        pendingCredential.AttemptsUsed++;
                        credentialManager.UpdateCredential(pendingCredential);

                        ShowStatus($"Invalid code. {pendingCredential.MaxAttempts - pendingCredential.AttemptsUsed} attempts remaining", true);
                        TotpCodeTextBox.Clear();
                        TotpCodeTextBox.Focus();
                        isAuthenticating = false;
                        EnableInputs();
                        HideLoading();

                        if (pendingCredential.AttemptsUsed >= pendingCredential.MaxAttempts)
                        {
                            await Task.Delay(2000);
                            MessageBox.Show("Maximum attempts exceeded. Application will now close.",
                                "Access Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                            Application.Current.Shutdown();
                        }
                        return;
                    }

                    // TOTP valid, mark credential as used and launch exam
                    ShowLoading("Launching exam interface...");

                    pendingCredential.IsUsed = true;
                    pendingCredential.AttemptsUsed++;
                    credentialManager.UpdateCredential(pendingCredential);

                    await LaunchExam(pendingCredential);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Error: {ex.Message}", true);
                isAuthenticating = false;
                EnableInputs();
                HideLoading();
            }
        }

        private async Task LaunchExam(StudentCredential credential)
        {
            try
            {
                // Create exam window
                var examWindow = new ExamWindow();

                // Start the exam session
                bool success = await examWindow.StartExam(
                    credential.StudentId,
                    credential.AccessToken,
                    credential.ExamId);

                if (success)
                {
                    // Show exam window first
                    examWindow.Show();

                    // Wait a moment for window to be fully rendered
                    await Task.Delay(100);

                    // Then hide login window
                    this.Hide();

                    // Set exam window as main window
                    Application.Current.MainWindow = examWindow;

                    // Handle exam window close
                    examWindow.Closed += (s, e) => Application.Current.Shutdown();
                }
                else
                {
                    // If exam start failed, re-enable login
                    ShowStatus("Failed to start exam. Please try again or contact support.", true);
                    isAuthenticating = false;
                    EnableInputs();
                    HideLoading();
                    this.Show();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch exam:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                isAuthenticating = false;
                EnableInputs();
                HideLoading();
                this.Show();
            }
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusIcon.Text = isError ? "⚠" : "ℹ";

            if (isError)
            {
                StatusBorder.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(254, 226, 226));
                StatusBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("ErrorBrush");
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(127, 29, 29));
            }
            else
            {
                StatusBorder.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(219, 234, 254));
                StatusBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("PrimaryBrush");
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(30, 64, 175));
            }

            StatusBorder.Visibility = Visibility.Visible;
        }

        private void ShowLoading(string message)
        {
            LoadingText.Text = message;
            LoadingPanel.Visibility = Visibility.Visible;
        }

        private void HideLoading()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }

        private void DisableInputs()
        {
            StudentIdTextBox.IsEnabled = false;
            AccessTokenBox.IsEnabled = false;
            TotpCodeTextBox.IsEnabled = false;
            LoginButton.IsEnabled = false;
        }

        private void EnableInputs()
        {
            if (TotpPanel.Visibility == Visibility.Visible)
            {
                StudentIdTextBox.IsEnabled = false;
                AccessTokenBox.IsEnabled = false;
                TotpCodeTextBox.IsEnabled = true;
                LoginButton.IsEnabled = true;
            }
            else
            {
                StudentIdTextBox.IsEnabled = true;
                AccessTokenBox.IsEnabled = true;
                TotpCodeTextBox.IsEnabled = true;
                LoginButton.IsEnabled = true;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e) { }
        private void TextBox_LostFocus(object sender, RoutedEventArgs e) { }
        private void PasswordBox_GotFocus(object sender, RoutedEventArgs e) { }
        private void PasswordBox_LostFocus(object sender, RoutedEventArgs e) { }
        private void SessionTokenBox_PasswordChanged(object sender, RoutedEventArgs e) { }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (isAuthenticating)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }
    }
}