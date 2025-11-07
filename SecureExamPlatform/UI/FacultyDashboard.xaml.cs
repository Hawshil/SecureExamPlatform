using Microsoft.Win32;
using SecureExamPlatform.Core;
using SecureExamPlatform.Models;
using SecureExamPlatform.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace SecureExamPlatform.UI
{
    public partial class FacultyDashboard : Window
    {
        private CredentialManager credentialManager;
        private string selectedFilePath;
        private DispatcherTimer totpUpdateTimer;

        public FacultyDashboard()
        {
            InitializeComponent();
            credentialManager = new CredentialManager();
            LoadExams();
            SetupTotpTimer();
        }

        private void LoadExams()
        {
            // Load available exams
            ExamSelector.Items.Add("CSE101-MID-2025");
            ExamSelector.Items.Add("CSE102-FINAL-2025");
            ExamSelector.SelectedIndex = 0;
        }

        private void SetupTotpTimer()
        {
            totpUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            totpUpdateTimer.Tick += (s, e) =>
            {
                if (CredentialsDisplay.Visibility == Visibility.Visible &&
                    !string.IsNullOrEmpty(GenTotpSecret.Text))
                {
                    GenCurrentTotp.Text = TotpManager.GenerateCode(GenTotpSecret.Text);
                }
            };
        }

        private void CreateExam_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Exam creation interface coming soon!", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ImportQuestions_Click(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedItem = ImportTab;
        }

        private void GenerateCredentials_Click(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedItem = CredentialsTab;
        }

        private void ViewStudents_Click(object sender, RoutedEventArgs e)
        {
            var credentials = credentialManager.GetAllCredentials();
            string message = $"Total Credentials: {credentials.Count}\n\n";
            foreach (var cred in credentials)
            {
                message += $"{cred.StudentId} - {(cred.IsUsed ? "Used" : "Active")}\n";
            }
            MessageBox.Show(message, "Student Credentials", MessageBoxButton.OK);
        }

        private void SelectFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Select Question Bank JSON"
            };

            if (dialog.ShowDialog() == true)
            {
                selectedFilePath = dialog.FileName;
                SelectedFileText.Text = $"Selected: {Path.GetFileName(selectedFilePath)}";
                ImportBtn.IsEnabled = true;
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string json = File.ReadAllText(selectedFilePath);
                var examContent = JsonSerializer.Deserialize<ExamContent>(json);

                // Save to exams folder
                Directory.CreateDirectory("Exams");
                string outputPath = Path.Combine("Exams", $"{examContent.ExamId}.json");
                File.Copy(selectedFilePath, outputPath, true);

                ImportStatus.Text = $"✓ Successfully imported {examContent.Questions.Count} questions for {examContent.Title}";
                ImportStatus.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");

                MessageBox.Show($"Exam '{examContent.Title}' imported successfully!\n\n" +
                    $"Questions: {examContent.Questions.Count}\n" +
                    $"Duration: {examContent.Duration} minutes",
                    "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ImportStatus.Text = $"✗ Import failed: {ex.Message}";
                ImportStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            }
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            string studentId = StudentIdInput.Text.Trim();
            string examId = ExamSelector.SelectedItem?.ToString();
            string hardwareId = HardwareIdInput.Text.Trim();
            string computerName = ComputerNameInput.Text.Trim();

            if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(hardwareId))
            {
                MessageBox.Show("Please enter Student ID and Hardware ID", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(computerName))
            {
                computerName = "Unknown Computer";
            }

            try
            {
                var credential = credentialManager.GenerateCredential(
                    studentId, examId, hardwareId, computerName, 180);

                // Display credentials
                GenStudentId.Text = credential.StudentId;
                GenAccessToken.Text = credential.AccessToken;
                GenTotpSecret.Text = credential.TotpSecret;
                GenCurrentTotp.Text = TotpManager.GenerateCode(credential.TotpSecret);
                GenValidUntil.Text = credential.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

                CredentialsDisplay.Visibility = Visibility.Visible;
                totpUpdateTimer.Start();

                MessageBox.Show("Credentials generated successfully!\n\n" +
                    "⚠ IMPORTANT: These credentials are hardware-bound and single-use.\n" +
                    "After login, they will be automatically invalidated.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating credentials: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyCredentials_Click(object sender, RoutedEventArgs e)
        {
            string details = $"Student ID: {GenStudentId.Text}\n" +
                           $"Access Token: {GenAccessToken.Text}\n" +
                           $"TOTP Secret: {GenTotpSecret.Text}\n" +
                           $"Current Code: {GenCurrentTotp.Text}\n" +
                           $"Valid Until: {GenValidUntil.Text}";

            Clipboard.SetText(details);
            MessageBox.Show("Credentials copied to clipboard!", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
