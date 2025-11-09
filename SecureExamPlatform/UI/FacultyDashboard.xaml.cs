using Microsoft.Win32;
using SecureExamPlatform.Core;
using SecureExamPlatform.Models;
using SecureExamPlatform.Grading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Text;
using System.Windows.Controls;

namespace SecureExamPlatform.UI
{
    public partial class FacultyDashboard : Window
    {
        private const string FACULTY_PASSWORD = "admin123";
        private SimpleCredentialManager credentialManager;
        private string examsDirectory;
        private List<ExamContent> availableExams;
        private List<StudentCredentialInfo> currentBatchCredentials;
        private GradingTool gradingTool;

        public class StudentCredentialInfo
        {
            public string StudentId { get; set; }
            public string LoginCode { get; set; }
            public string ExamId { get; set; }
        }

        public FacultyDashboard()
        {
            InitializeComponent();

            if (!AuthenticateFaculty())
            {
                this.Close();
                return;
            }

            credentialManager = new SimpleCredentialManager();
            gradingTool = new GradingTool();
            availableExams = new List<ExamContent>();
            currentBatchCredentials = new List<StudentCredentialInfo>();

            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            examsDirectory = Path.Combine(appDirectory, "Exams");
            Directory.CreateDirectory(examsDirectory);

            // Set window to fullscreen
            this.WindowState = WindowState.Maximized;

            LoadExams();
        }

        private bool AuthenticateFaculty()
        {
            var passwordDialog = new Window
            {
                Title = "Faculty Authentication",
                Height = 250,
                Width = 450,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = System.Windows.Media.Brushes.White,
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel
            {
                Margin = new Thickness(30),
                VerticalAlignment = VerticalAlignment.Center
            };

            var title = new TextBlock
            {
                Text = "🔐 Faculty Login",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var subtitle = new TextBlock
            {
                Text = "Enter your password to access the dashboard",
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var passwordBox = new PasswordBox
            {
                Height = 45,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 20)
            };

            var loginButton = new Button
            {
                Content = "Login",
                Height = 45,
                FontSize = 14,
                Background = System.Windows.Media.Brushes.Green,
                Foreground = System.Windows.Media.Brushes.White
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
                    loginButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };

            stack.Children.Add(title);
            stack.Children.Add(subtitle);
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
                GradingExamSelector.Items.Clear();

                var examFiles = Directory.GetFiles(examsDirectory, "*.json");

                if (examFiles.Length == 0)
                {
                    ExamSelector.Items.Add("No exams available - Upload an exam first");
                    GradingExamSelector.Items.Add("No exams available");
                    ExamSelector.IsEnabled = false;
                    GradingExamSelector.IsEnabled = false;
                    return;
                }

                ExamSelector.IsEnabled = true;
                GradingExamSelector.IsEnabled = true;

                foreach (var file in examFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var exam = DeserializeExamWithDurationFix(json);

                        if (exam != null)
                        {
                            availableExams.Add(exam);
                            string displayText = $"{exam.ExamId} - {exam.Title}";
                            ExamSelector.Items.Add(displayText);
                            GradingExamSelector.Items.Add(displayText);
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
                    GradingExamSelector.SelectedIndex = 0;
                }

                UpdateStatus($"✓ Loaded {availableExams.Count} exam(s)", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading exams: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ExamContent DeserializeExamWithDurationFix(string json)
        {
            try
            {
                // First, parse as JsonDocument to handle duration specially
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    var exam = new ExamContent
                    {
                        ExamId = root.GetProperty("examId").GetString(),
                        Title = root.GetProperty("title").GetString(),
                        Questions = new List<Question>()
                    };

                    // Handle duration - can be either number (minutes) or string (TimeSpan format)
                    if (root.TryGetProperty("duration", out JsonElement durationElement))
                    {
                        if (durationElement.ValueKind == JsonValueKind.Number)
                        {
                            exam.Duration = TimeSpan.FromMinutes(durationElement.GetInt32());
                        }
                        else if (durationElement.ValueKind == JsonValueKind.String)
                        {
                            if (TimeSpan.TryParse(durationElement.GetString(), out TimeSpan ts))
                            {
                                exam.Duration = ts;
                            }
                        }
                    }

                    // Parse questions
                    if (root.TryGetProperty("questions", out JsonElement questionsElement))
                    {
                        foreach (var qElement in questionsElement.EnumerateArray())
                        {
                            var question = new Question
                            {
                                Id = qElement.GetProperty("id").GetString(),
                                Text = qElement.GetProperty("text").GetString(),
                                Points = qElement.TryGetProperty("marks", out var marksElem) ? marksElem.GetInt32() :
                                        qElement.TryGetProperty("points", out var pointsElem) ? pointsElem.GetInt32() : 1
                            };

                            // Handle type
                            if (qElement.TryGetProperty("type", out var typeElem))
                            {
                                string typeStr = typeElem.GetString()?.ToUpper() ?? "ESSAY";
                                question.Type = typeStr switch
                                {
                                    "MCQ" or "MULTIPLECHOICE" => QuestionType.MultipleChoice,
                                    "TRUEFALSE" => QuestionType.TrueFalse,
                                    "FILLINTHEBLANK" => QuestionType.FillInTheBlank,
                                    _ => QuestionType.Essay
                                };
                            }

                            // Handle options for MCQ
                            if (qElement.TryGetProperty("options", out var optionsElem))
                            {
                                foreach (var option in optionsElem.EnumerateArray())
                                {
                                    question.Options.Add(option.GetString());
                                }
                            }

                            // Handle correct answer
                            if (qElement.TryGetProperty("correctAnswer", out var answerElem))
                            {
                                question.CorrectAnswer = answerElem.GetString();
                            }

                            exam.Questions.Add(question);
                        }
                    }

                    return exam;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse exam: {ex.Message}");
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
                var exam = DeserializeExamWithDurationFix(json);

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
                if (exam.Duration.TotalMinutes == 0)
                    throw new Exception("Exam must have a valid duration");

                // Save with properly formatted JSON
                string safeExamId = string.Join("_", exam.ExamId.Split(Path.GetInvalidFileNameChars()));
                string outputPath = Path.Combine(examsDirectory, $"{safeExamId}.json");

                // Create a properly formatted JSON with duration as minutes
                var jsonObject = new
                {
                    examId = exam.ExamId,
                    title = exam.Title,
                    duration = (int)exam.Duration.TotalMinutes,
                    questions = exam.Questions.Select(q => new
                    {
                        id = q.Id,
                        text = q.Text,
                        type = q.Type.ToString(),
                        options = q.Options,
                        correctAnswer = q.CorrectAnswer,
                        marks = q.Points
                    })
                };

                string formattedJson = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions
                {
                    WriteIndented = true
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
                    $"This is a single-use code.",
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

                BatchCodesOutput.Text = sb.ToString();
                BatchCodesOutputBorder.Visibility = Visibility.Visible;

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

        private void GradeExam_Click(object sender, RoutedEventArgs e)
        {
            string selectedExam = GradingExamSelector.SelectedItem?.ToString();

            if (string.IsNullOrEmpty(selectedExam) || selectedExam.Contains("No exams"))
            {
                MessageBox.Show("Please select an exam to grade", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string examId = selectedExam.Split('-')[0].Trim();

                GradingStatusText.Text = "Grading in progress...";
                GradingResultsPanel.Visibility = Visibility.Collapsed;

                var results = gradingTool.GradeExam(examId);

                if (results.Count == 0)
                {
                    GradingStatusText.Text = "No submissions found for this exam.";
                    return;
                }

                // Display results
                var sb = new StringBuilder();
                sb.AppendLine($"📊 Grading Results for {examId}");
                sb.AppendLine($"Total Submissions: {results.Count}");
                sb.AppendLine();
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"{"Student ID",-15} | MCQ Score | Time Spent");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                foreach (var result in results.OrderBy(r => r.StudentId))
                {
                    double mcqPercent = result.TotalMcqMarks > 0
                        ? (result.EarnedMcqMarks * 100.0 / result.TotalMcqMarks)
                        : 0;

                    sb.AppendLine($"{result.StudentId,-15} | {result.EarnedMcqMarks}/{result.TotalMcqMarks} ({mcqPercent:F1}%) | {result.TimeSpent.TotalMinutes:F0}m");
                }

                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine();

                // Statistics
                double avgScore = results.Average(r =>
                    r.TotalMcqMarks > 0 ? (r.EarnedMcqMarks * 100.0 / r.TotalMcqMarks) : 0);

                sb.AppendLine($"Average MCQ Score: {avgScore:F1}%");
                sb.AppendLine($"Highest Score: {results.Max(r => r.TotalMcqMarks > 0 ? (r.EarnedMcqMarks * 100.0 / r.TotalMcqMarks) : 0):F1}%");
                sb.AppendLine($"Lowest Score: {results.Min(r => r.TotalMcqMarks > 0 ? (r.EarnedMcqMarks * 100.0 / r.TotalMcqMarks) : 0):F1}%");

                GradingResultsText.Text = sb.ToString();
                GradingResultsPanel.Visibility = Visibility.Visible;
                GradingStatusText.Text = $"✓ Graded {results.Count} submissions successfully";

                // Store results for export
                Tag = results;
            }
            catch (Exception ex)
            {
                GradingStatusText.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Grading failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportGradingReport_Click(object sender, RoutedEventArgs e)
        {
            if (Tag is not List<GradingTool.GradingResult> results || results.Count == 0)
            {
                MessageBox.Show("No grading results to export. Grade an exam first.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                gradingTool.GenerateReport(results);
                gradingTool.ExportToCSV(results);
                MessageBox.Show("Reports exported to Desktop!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
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