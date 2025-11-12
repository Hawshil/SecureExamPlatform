using SecureExam.Core.Core;
using SecureExam.Core.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace SecureExam.Student
{
    public partial class StudentLoginWindow : Window
    {
        private LabCredentialManager credentialManager;
        private bool isProcessing = false;

        public StudentLoginWindow()
        {
            InitializeComponent();
            credentialManager = new LabCredentialManager();

            // Focus student ID box on load
            Loaded += (s, e) => StudentIdBox.Focus();

            // Enter key handling
            StudentIdBox.KeyDown += InputBox_KeyDown;
            LoginCodeBox.KeyDown += InputBox_KeyDown;
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Move to next field or login
                if (sender == StudentIdBox)
                {
                    LoginCodeBox.Focus();
                }
                else if (sender == LoginCodeBox)
                {
                    LoginButton_Click(null, null);
                }
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (isProcessing) return;

            string studentId = StudentIdBox.Text.Trim().ToUpper();
            string loginCode = LoginCodeBox.Text.Trim().ToUpper();

            if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(loginCode))
            {
                StatusText.Text = "⚠️ Please enter both Student ID and Login Code";
                StatusText.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            isProcessing = true;
            LoginButton.IsEnabled = false;
            LoginButton.Content = "Validating...";
            StatusText.Text = "🔄 Validating credentials...";
            StatusText.Foreground = System.Windows.Media.Brushes.Cyan;

            try
            {
                var result = credentialManager.ValidateStudentLogin(studentId, loginCode);

                if (result.Success)
                {
                    StatusText.Text = "✓ Valid! Preparing exam...";
                    StatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;

                    // Terminate previous sessions
                    await credentialManager.TerminatePreviousSession(studentId);

                    // Load exam
                    string examPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SecureExam",
                        "Exams",
                        $"{result.ExamId}.json"
                    );

                    if (!File.Exists(examPath))
                    {
                        StatusText.Text = $"❌ Exam file not found: {result.ExamId}";
                        StatusText.Foreground = System.Windows.Media.Brushes.Red;
                        LoginButton.IsEnabled = true;
                        LoginButton.Content = "Start Exam";
                        isProcessing = false;
                        return;
                    }

                    string examJson = File.ReadAllText(examPath);
                    var examContent = JsonSerializer.Deserialize<ExamContent>(examJson);

                    if (examContent == null || examContent.Questions == null || examContent.Questions.Count == 0)
                    {
                        StatusText.Text = "❌ Invalid exam content";
                        StatusText.Foreground = System.Windows.Media.Brushes.Red;
                        LoginButton.IsEnabled = true;
                        LoginButton.Content = "Start Exam";
                        isProcessing = false;
                        return;
                    }

                    // Mark credential as used
                    credentialManager.MarkCredentialAsUsed(studentId, loginCode);
                    credentialManager.CreateActiveSession(studentId);

                    // Launch exam window
                    var examWindow = new SecureExamWindow(studentId, result.ExamId, result.LabId, examContent);
                    examWindow.Show();
                    this.Close();
                }
                else
                {
                    StatusText.Text = $"❌ {result.Message}";
                    StatusText.Foreground = System.Windows.Media.Brushes.Red;
                    LoginButton.IsEnabled = true;
                    LoginButton.Content = "Start Exam";
                    LoginCodeBox.Clear();
                    LoginCodeBox.Focus();
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                LoginButton.IsEnabled = true;
                LoginButton.Content = "Start Exam";
            }
            finally
            {
                isProcessing = false;
            }
        }
    }
}
