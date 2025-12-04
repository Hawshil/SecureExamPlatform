using SecureExam.Core;
using SecureExam.Core.Core;
using SecureExam.Core.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SecureExam.Student
{
    public partial class StudentLoginWindow : Window
    {
        public StudentLoginWindow()
        {
            InitializeComponent();
            StudentIdBox.Focus();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = StudentIdBox.Text.Trim();
            string loginCode = LoginCodeBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(studentId))
            {
                StatusText.Text = "Please enter Student ID";
                StudentIdBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(loginCode))
            {
                StatusText.Text = "Please enter Login Code";
                LoginCodeBox.Focus();
                return;
            }

            try
            {
                // Validate credentials using LabCredentialManager
                var credManager = new LabCredentialManager();
                var validationResult = credManager.ValidateStudentLogin(studentId, loginCode);

                if (!validationResult.Success)
                {
                    StatusText.Text = validationResult.Message;
                    return;
                }

                // Get exam ID and lab ID from validation result
                string examId = validationResult.ExamId;
                string labId = validationResult.LabId;

                // Load exam content
                string examsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Exams",
                    $"{examId}.json"
                );

                if (!File.Exists(examsPath))
                {
                    StatusText.Text = $"Exam file not found! Please contact your instructor.";
                    return;
                }

                string json = File.ReadAllText(examsPath);
                var exam = JsonSerializer.Deserialize<ExamContent>(json);

                if (exam == null || exam.Questions == null || exam.Questions.Count == 0)
                {
                    StatusText.Text = "Invalid exam content!";
                    return;
                }

                // Mark credential as used
                credManager.MarkCredentialAsUsed(studentId, loginCode);

                // Terminate any previous sessions
                credManager.TerminatePreviousSession(studentId).Wait();

                // Create new active session
                credManager.CreateActiveSession(studentId);

                // Open secure exam window
                var examWindow = new SecureExamWindow(studentId, examId, labId, exam);
                examWindow.Show();
                this.Close();
            }
            catch (JsonException)
            {
                StatusText.Text = "Invalid exam file format!";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void StudentIdBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                LoginCodeBox.Focus();
            }
        }

        private void ExamIdBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                LoginCodeBox.Focus();
            }
        }

        private void LabIdBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                LoginButton_Click(sender, e);
            }
        }
    }
}
