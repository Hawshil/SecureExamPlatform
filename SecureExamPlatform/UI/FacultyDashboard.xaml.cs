using Microsoft.Win32;
using SecureExamPlatform.Core;
using SecureExamPlatform.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Text;

namespace SecureExamPlatform.UI
{
    public partial class FacultyDashboard : Window
    {
        private const string FACULTY_PASSWORD = "admin123"; // Change this in production
        private SimpleCredentialManager credentialManager;
        private string examsDirectory;
        private List<ExamContent> availableExams;
        private List<StudentCredentialInfo> currentBatchCredentials;

        public class StudentCredentialInfo
        {
            public string StudentId { get; set; }
            public string LoginCode { get; set; }
            public string ExamId { get; set; }
        }

        public FacultyDashboard()
        {
            InitializeComponent();

            // Authenticate on load
            if (!AuthenticateFaculty())
            {
                this.Close();
                return;
            }

            credentialManager = new SimpleCredentialManager();
            availableExams = new List<ExamContent>();
            currentBatchCredentials = new List<StudentCredentialInfo>();

            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            examsDirectory = Path.Combine(appDirectory, "Exams");
            Directory.CreateDirectory(examsDirectory);

            LoadExams();
        }

        private bool AuthenticateFaculty()
        {
            var passwordDialog = new Window
            {
                Title = "Faculty Authentication",
                Height = 200,
                Width = 400,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = System.Windows.Media.Brushes.White,
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Center
            };

            var title = new System.Windows.Controls.TextBlock
            {
                Text = "Enter Faculty Password",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var passwordBox = new System.Windows.Controls.PasswordBox
            {
                Height = 40,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 20)
            };

            var loginButton = new System.Windows.Controls.Button
            {
                Content = "Login",
                Height = 40,
                FontSize = 14
            };

            bool authenticated = false;

            loginButton.Click += (s, e) =>
            {
                if (passwordBox.Password == FACULTY_PASSWORD)
                {
                    authenticated = true;
                    passwordDialog.Close();
                }
                else
                {
                    MessageBox.Show("Incorrect password. Please try again.", "Authentication Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    passwordBox.Clear();
                    passwordBox.Focus();
                }
            };

            passwordBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    loginButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                }
            };

            stack.Children.Add(title);
            stack.Children.Add(passwordBox);
            stack.Children.Add(loginButton);
            passwordDialog.Content = stack;

            passwordBox.Focus();
            passwordDialog.ShowDialog();

            return authenticated;
        }

        private void LoadExams()
        {
            try
            {
                availableExams.Clear();
                ExamSelector.Items.Clear();

                var examFiles = Directory.GetFiles(examsDirectory, "*.json");

                if (examFiles.Length == 0)
                {
                    ExamSelector.Items.Add("No exams available - Upload an exam first");
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
                            // Fix duration if needed
                            if (exam.Duration.TotalMinutes == 0)
                            {
                                using (JsonDocument doc = JsonDocument.Parse(json))
                                {
                                    if (doc.RootElement.TryGetProperty("duration", out JsonElement durationElement))
                                    {
                                        if (durationElement.ValueKind == JsonValueKind.Number)
                                        {
                                            exam.Duration = TimeSpan.FromMinutes(durationElement.GetInt32());
                                        }
                                    }
                                }
                            }

                            availableExams.Add(exam);
                            ExamSelector.Items.Add($"{exam.ExamId} - {exam.Title}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error loading exam {Path.GetFileName(file)}: {ex.Message}", "Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                if (ExamSelector.Items.Count > 0)
                {
                    ExamSelector.SelectedIndex = 0;
                }

                UpdateStatus($"✓ Loaded {availableExams.Count} exam(s)", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading exams: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UploadExam_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    Title = "Select Exam JSON File"
                };

                if (dialog.ShowDialog() != true) return;

                string json = File.ReadAllText(dialog.FileName);
                var exam = JsonSerializer.Deserialize<ExamContent>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (exam == null)
                {
                    throw new Exception("Failed to parse JSON file");
                }

                // Validate
                if (string.IsNullOrEmpty(exam.ExamId))
                    throw new Exception("Exam must have an 'examId'");
                if (string.IsNullOrEmpty(exam.Title))
                    throw new Exception("Exam must have a 'title'");
                if (exam.Questions == null || exam.Questions.Count == 0)
                    throw new Exception("Exam must have at least one question");

                // Handle duration
                if (exam.Duration.TotalMinutes == 0)
                {
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("duration", out JsonElement durationElement))
                        {
                            if (durationElement.ValueKind == JsonValueKind.Number)
                            {
                                exam.Duration = TimeSpan.FromMinutes(durationElement.GetInt32());
                            }
                        }
                    }
                }

                if (exam.Duration.TotalMinutes == 0)
                    throw new Exception("Exam must have a valid duration");

                // Validate questions
                foreach (var q in exam.Questions)
                {
                    if (string.IsNullOrEmpty(q.Id))
                        throw new Exception($"All questions must have an 'id'");
                    if (string.IsNullOrEmpty(q.Text))
                        throw new Exception($"Question {q.Id} must have 'text'");
                    if (q.Type == 0)
                        q.Type = QuestionType.Essay;
                    if (q.Type == QuestionType.MultipleChoice && (q.Options == null || q.Options.Count < 2))
                        throw new Exception($"MCQ Question {q.Id} must have at least 2 options");
                }

                // Save
                string safeExamId = string.Join("_", exam.ExamId.Split(Path.GetInvalidFileNameChars()));
                string outputPath = Path.Combine(examsDirectory, $"{safeExamId}.json");

                string formattedJson = JsonSerializer.Serialize(exam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                File.WriteAllText(outputPath, formattedJson);

                LoadExams();

                MessageBox.Show(
                    $"✓ Exam uploaded successfully!\n\n" +
                    $"Exam: {exam.Title}\n" +
                    $"ID: {exam.ExamId}\n" +
                    $"Questions: {exam.Questions.Count}\n" +
                    $"Duration: {exam.Duration.TotalMinutes} minutes",
                    "Upload Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Upload failed:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateSingleCode_Click(object sender, RoutedEventArgs e)
        {
            string studentId = SingleStudentIdBox.Text.Trim();
            string selectedExam = ExamSelector.SelectedItem?.ToString();

            if (string.IsNullOrEmpty(studentId))
            {
                MessageBox.Show("Please enter Student ID", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(selectedExam) || selectedExam.Contains("No exams"))
            {
                MessageBox.Show("Please upload an exam first", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string examId = selectedExam.Split('-')[0].Trim();
                var credential = credentialManager.GenerateCredential(studentId, examId, allowedLogins: 1);

                MessageBox.Show(
                    $"✓ Login Code Generated!\n\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"Student ID: {credential.StudentId}\n" +
                    $"Login Code: {credential.LoginCode}\n" +
                    $"Exam: {examId}\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                    $"Give this code to the student.\n" +
                    $"This is a single-use code that expires after first login.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                SingleStudentIdBox.Clear();
                UpdateStatus($"✓ Code generated for {studentId}", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateBatchCodes_Click(object sender, RoutedEventArgs e)
        {
            string studentIds = BatchStudentIdsBox.Text.Trim();
            string selectedExam = ExamSelector.SelectedItem?.ToString();

            if (string.IsNullOrEmpty(studentIds))
            {
                MessageBox.Show("Please enter student IDs (one per line)", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(selectedExam) || selectedExam.Contains("No exams"))
            {
                MessageBox.Show("Please upload an exam first", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string examId = selectedExam.Split('-')[0].Trim();
                var studentIdList = studentIds.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(s => s.Trim())
                                              .Where(s => !string.IsNullOrEmpty(s))
                                              .ToList();

                if (studentIdList.Count == 0)
                {
                    MessageBox.Show("No valid student IDs found", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                currentBatchCredentials.Clear();

                foreach (var studentId in studentIdList)
                {
                    var credential = credentialManager.GenerateCredential(studentId, examId, allowedLogins: 1);
                    currentBatchCredentials.Add(new StudentCredentialInfo
                    {
                        StudentId = credential.StudentId,
                        LoginCode = credential.LoginCode,
                        ExamId = credential.ExamId
                    });
                }

                // Display codes
                var sb = new StringBuilder();
                sb.AppendLine($"✓ Generated codes for {currentBatchCredentials.Count} students:");
                sb.AppendLine();
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"{"Student ID",-15} | Login Code");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                foreach (var cred in currentBatchCredentials)
                {
                    sb.AppendLine($"{cred.StudentId,-15} | {cred.LoginCode}");
                }

                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine();
                sb.AppendLine("These codes are single-use and expire after first login.");

                BatchCodesOutput.Text = sb.ToString();
                BatchCodesOutput.Visibility = Visibility.Visible;

                UpdateStatus($"✓ Generated {currentBatchCredentials.Count} codes", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating batch codes: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyBatchCodes_Click(object sender, RoutedEventArgs e)
        {
            if (currentBatchCredentials.Count == 0)
            {
                MessageBox.Show("No codes to copy. Generate codes first.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Clipboard.SetText(BatchCodesOutput.Text);
                MessageBox.Show("✓ Codes copied to clipboard!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportBatchCodes_Click(object sender, RoutedEventArgs e)
        {
            if (currentBatchCredentials.Count == 0)
            {
                MessageBox.Show("No codes to export. Generate codes first.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv",
                    FileName = $"LoginCodes_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() != true) return;

                if (saveDialog.FileName.EndsWith(".csv"))
                {
                    // Export as CSV
                    var sb = new StringBuilder();
                    sb.AppendLine("Student ID,Login Code,Exam ID");
                    foreach (var cred in currentBatchCredentials)
                    {
                        sb.AppendLine($"{cred.StudentId},{cred.LoginCode},{cred.ExamId}");
                    }
                    File.WriteAllText(saveDialog.FileName, sb.ToString());
                }
                else
                {
                    // Export as formatted text
                    File.WriteAllText(saveDialog.FileName, BatchCodesOutput.Text);
                }

                MessageBox.Show($"✓ Codes exported to:\n{saveDialog.FileName}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusText.Foreground = isError
                ? System.Windows.Media.Brushes.Red
                : System.Windows.Media.Brushes.Green;
        }
    }
}