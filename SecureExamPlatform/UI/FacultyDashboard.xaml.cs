using Microsoft.Win32;
using SecureExamPlatform.Core;
using SecureExamPlatform.Models;
using SecureExamPlatform.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private string examsDirectory;
        private List<ExamContent> availableExams;

        public FacultyDashboard()
        {
            InitializeComponent();
            credentialManager = new CredentialManager();
            availableExams = new List<ExamContent>();

            // Setup exams directory
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            examsDirectory = Path.Combine(appDirectory, "Exams");
            Directory.CreateDirectory(examsDirectory);

            LoadExams();
            SetupTotpTimer();
        }

        private void LoadExams()
        {
            try
            {
                availableExams.Clear();
                ExamSelector.Items.Clear();
                ActiveExamsList.Items.Clear();

                var examFiles = Directory.GetFiles(examsDirectory, "*.json");

                if (examFiles.Length == 0)
                {
                    ExamSelector.Items.Add("No exams available - Import exams first");
                    ExamSelector.IsEnabled = false;
                    return;
                }

                ExamSelector.IsEnabled = true;

                foreach (var file in examFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var exam = JsonSerializer.Deserialize<ExamContent>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (exam != null)
                        {
                            availableExams.Add(exam);
                            ExamSelector.Items.Add($"{exam.ExamId} - {exam.Title}");
                            ActiveExamsList.Items.Add($"📄 {exam.Title}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading exam {file}: {ex.Message}");
                    }
                }

                if (ExamSelector.Items.Count > 0)
                {
                    ExamSelector.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading exams: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetupTotpTimer()
        {
            totpUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            totpUpdateTimer.Tick += (s, e) =>
            {
                if (CredentialsDisplay.Visibility == Visibility.Visible &&
                    !string.IsNullOrEmpty(GenTotpSecret.Text))
                {
                    try
                    {
                        GenCurrentTotp.Text = TotpManager.GenerateCode(GenTotpSecret.Text);
                    }
                    catch { }
                }
            };
        }

        private void CreateExam_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "To create an exam, prepare a JSON file with the following structure:\n\n" +
                "{\n" +
                "  \"examId\": \"EXAM-001\",\n" +
                "  \"title\": \"Your Exam Title\",\n" +
                "  \"duration\": 90,\n" +
                "  \"questions\": [\n" +
                "    {\n" +
                "      \"id\": \"q1\",\n" +
                "      \"text\": \"Question text?\",\n" +
                "      \"type\": \"MCQ\",\n" +
                "      \"options\": [\"A\", \"B\", \"C\", \"D\"],\n" +
                "      \"correctAnswer\": \"A\",\n" +
                "      \"marks\": 2\n" +
                "    }\n" +
                "  ]\n" +
                "}\n\n" +
                "Then use 'Import Questions' to upload it.",
                "Create Exam",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ImportQuestions_Click(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedItem = ImportTab;
        }

        private void GenerateCredentials_Click(object sender, RoutedEventArgs e)
        {
            if (ExamSelector.Items.Count == 0 || !ExamSelector.IsEnabled)
            {
                MessageBox.Show(
                    "No exams available. Please import an exam first using the 'Import Questions' tab.",
                    "No Exams",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            MainTabControl.SelectedItem = CredentialsTab;
        }

        private void ViewStudents_Click(object sender, RoutedEventArgs e)
        {
            var credentials = credentialManager.GetAllCredentials();

            if (credentials.Count == 0)
            {
                MessageBox.Show(
                    "No credentials generated yet.\n\n" +
                    "Use the 'Generate Credentials' tab to create credentials for students.",
                    "No Credentials",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string message = $"Total Credentials: {credentials.Count}\n" +
                           $"Active: {credentials.Count(c => !c.IsUsed)}\n" +
                           $"Used: {credentials.Count(c => c.IsUsed)}\n\n";

            message += "Recent Credentials:\n";
            message += new string('-', 50) + "\n";

            foreach (var cred in credentials.OrderByDescending(c => c.CreatedAt).Take(10))
            {
                string status = cred.IsUsed ? "✓ Used" : "⏳ Active";
                string expired = DateTime.UtcNow > cred.ExpiresAt ? " (EXPIRED)" : "";
                message += $"{cred.StudentId} - {cred.ExamId} - {status}{expired}\n";
            }

            MessageBox.Show(message, "Student Credentials", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SelectFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Select Exam JSON File",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                selectedFilePath = dialog.FileName;
                SelectedFileText.Text = $"✓ Selected: {Path.GetFileName(selectedFilePath)}";
                SelectedFileText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                ImportBtn.IsEnabled = true;
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedFilePath) || !File.Exists(selectedFilePath))
            {
                ImportStatus.Text = "✗ Please select a valid file first";
                ImportStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
                return;
            }

            try
            {
                // Read and validate JSON
                string json = File.ReadAllText(selectedFilePath);
                var examContent = JsonSerializer.Deserialize<ExamContent>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (examContent == null)
                {
                    throw new Exception("Invalid JSON format");
                }

                // Validate required fields
                if (string.IsNullOrEmpty(examContent.ExamId))
                {
                    throw new Exception("Exam must have an 'examId' field");
                }

                if (string.IsNullOrEmpty(examContent.Title))
                {
                    throw new Exception("Exam must have a 'title' field");
                }

                if (examContent.Questions == null || examContent.Questions.Count == 0)
                {
                    throw new Exception("Exam must have at least one question");
                }

                // Handle duration (might be int in JSON)
                if (examContent.Duration.TotalMinutes == 0)
                {
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("duration", out JsonElement durationElement))
                        {
                            if (durationElement.ValueKind == JsonValueKind.Number)
                            {
                                int minutes = durationElement.GetInt32();
                                examContent.Duration = TimeSpan.FromMinutes(minutes);
                            }
                        }
                    }
                }

                if (examContent.Duration.TotalMinutes == 0)
                {
                    throw new Exception("Exam must have a valid duration (in minutes)");
                }

                // Validate questions
                int questionNumber = 1;
                foreach (var question in examContent.Questions)
                {
                    if (string.IsNullOrEmpty(question.Id))
                    {
                        throw new Exception($"Question {questionNumber} must have an 'id' field");
                    }

                    if (string.IsNullOrEmpty(question.Text))
                    {
                        throw new Exception($"Question {question.Id} must have a 'text' field");
                    }

                    // Set default question type if not specified
                    if (question.Type == 0)
                    {
                        question.Type = QuestionType.Essay;
                    }

                    // Validate MCQ questions have options
                    if (question.Type == QuestionType.MultipleChoice)
                    {
                        if (question.Options == null || question.Options.Count < 2)
                        {
                            throw new Exception($"MCQ Question {question.Id} must have at least 2 options");
                        }
                    }

                    questionNumber++;
                }

                // Save to exams directory with normalized filename
                string safeExamId = string.Join("_", examContent.ExamId.Split(Path.GetInvalidFileNameChars()));
                string outputPath = Path.Combine(examsDirectory, $"{safeExamId}.json");

                // Serialize with proper formatting
                string formattedJson = JsonSerializer.Serialize(examContent, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                File.WriteAllText(outputPath, formattedJson);

                ImportStatus.Text = $"✓ Successfully imported: {examContent.Title}\n" +
                                  $"  • Exam ID: {examContent.ExamId}\n" +
                                  $"  • Questions: {examContent.Questions.Count}\n" +
                                  $"  • Duration: {examContent.Duration.TotalMinutes} minutes\n" +
                                  $"  • Saved to: {outputPath}";
                ImportStatus.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");

                // Reload exams list
                LoadExams();

                MessageBox.Show(
                    $"✓ Exam imported successfully!\n\n" +
                    $"Exam: {examContent.Title}\n" +
                    $"ID: {examContent.ExamId}\n" +
                    $"Questions: {examContent.Questions.Count}\n" +
                    $"Duration: {examContent.Duration.TotalMinutes} minutes\n\n" +
                    $"You can now generate credentials for this exam.",
                    "Import Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Clear selection
                selectedFilePath = null;
                SelectedFileText.Text = "No file selected";
                SelectedFileText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                ImportBtn.IsEnabled = false;
            }
            catch (JsonException ex)
            {
                ImportStatus.Text = $"✗ JSON Parse Error: {ex.Message}\n\n" +
                                  "Please ensure your JSON is properly formatted.";
                ImportStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");

                MessageBox.Show($"Invalid JSON format:\n\n{ex.Message}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                ImportStatus.Text = $"✗ Import failed: {ex.Message}";
                ImportStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");

                MessageBox.Show($"Import failed:\n\n{ex.Message}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            string studentId = StudentIdInput.Text.Trim();
            string selectedExam = ExamSelector.SelectedItem?.ToString();
            string hardwareId = HardwareIdInput.Text.Trim();
            string computerName = ComputerNameInput.Text.Trim();

            // Validation
            if (string.IsNullOrEmpty(studentId))
            {
                MessageBox.Show("Please enter Student ID", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                StudentIdInput.Focus();
                return;
            }

            if (string.IsNullOrEmpty(selectedExam) || selectedExam.Contains("No exams"))
            {
                MessageBox.Show("Please import an exam first", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(hardwareId))
            {
                MessageBox.Show("Please enter Hardware ID", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                HardwareIdInput.Focus();
                return;
            }

            if (string.IsNullOrEmpty(computerName))
            {
                computerName = Environment.MachineName;
            }

            try
            {
                // Extract exam ID from selection (format: "EXAMID - Title")
                string examId = selectedExam.Split('-')[0].Trim();

                var credential = credentialManager.GenerateCredential(
                    studentId, examId, hardwareId, computerName, 180);

                // Display credentials
                GenStudentId.Text = credential.StudentId;
                GenAccessToken.Text = credential.AccessToken;
                GenTotpSecret.Text = credential.TotpSecret;
                GenCurrentTotp.Text = TotpManager.GenerateCode(credential.TotpSecret);

                // Convert to local time for display
                TimeZoneInfo localZone = TimeZoneInfo.Local;
                DateTime expiresLocal = TimeZoneInfo.ConvertTimeFromUtc(credential.ExpiresAt, localZone);
                GenValidUntil.Text = expiresLocal.ToString("yyyy-MM-dd HH:mm:ss");

                CredentialsDisplay.Visibility = Visibility.Visible;
                totpUpdateTimer.Start();

                MessageBox.Show(
                    "✓ Credentials generated successfully!\n\n" +
                    "IMPORTANT INSTRUCTIONS:\n\n" +
                    "1. Give the student their Access Token\n" +
                    "2. Have them scan the TOTP Secret with their authenticator app\n" +
                    "3. These credentials are:\n" +
                    "   • Hardware-bound (can only be used on this computer)\n" +
                    "   • Single-use (will be invalidated after login)\n" +
                    "   • Time-limited (expires after 180 minutes)\n\n" +
                    "4. Student must enter TOTP code from their app during login",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Clear inputs
                StudentIdInput.Clear();
                HardwareIdInput.Clear();
                ComputerNameInput.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating credentials:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyCredentials_Click(object sender, RoutedEventArgs e)
        {
            string details = $"=== EXAM CREDENTIALS ===\n\n" +
                           $"Student ID: {GenStudentId.Text}\n" +
                           $"Access Token: {GenAccessToken.Text}\n\n" +
                           $"TOTP Setup:\n" +
                           $"Secret Key: {GenTotpSecret.Text}\n" +
                           $"Current Code: {GenCurrentTotp.Text}\n\n" +
                           $"Valid Until: {GenValidUntil.Text}\n\n" +
                           $"INSTRUCTIONS:\n" +
                           $"1. Add TOTP secret to authenticator app\n" +
                           $"2. Use Student ID and Access Token to login\n" +
                           $"3. Enter 6-digit code from authenticator\n" +
                           $"4. Credentials are single-use only";

            Clipboard.SetText(details);
            MessageBox.Show("✓ Credentials copied to clipboard!", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}