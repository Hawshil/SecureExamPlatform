using Microsoft.Win32;
using SecureExamPlatform.Core;
using SecureExamPlatform.Models;
using SecureExamPlatform.Grading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace SecureExamPlatform.UI
{
    public partial class FacultyDashboard : Window
    {
        private const string FACULTY_PASSWORD = "admin123";
        private LabCredentialManager credentialManager;
        private string examsDirectory;
        private List<string> availableExams;
        private List<StudentCredentialInfo> currentBatchCredentials;
        private GradingTool gradingTool;

        public class StudentCredentialInfo
        {
            public string StudentId { get; set; }
            public string LoginCode { get; set; }
            public string ExamId { get; set; }
            public string LabId { get; set; }
            public string StudentName { get; set; }
        }

        public FacultyDashboard()
        {
            InitializeComponent();

            if (!AuthenticateFaculty())
            {
                this.Close();
                return;
            }

            credentialManager = new LabCredentialManager();
            gradingTool = new GradingTool();
            availableExams = new List<string>();
            currentBatchCredentials = new List<StudentCredentialInfo>();

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SecureExam"
            );
            examsDirectory = Path.Combine(appData, "Exams");
            Directory.CreateDirectory(examsDirectory);

            LoadExams();
        }

        private bool AuthenticateFaculty()
        {
            var passwordDialog = new PasswordDialog();
            if (passwordDialog.ShowDialog() == true)
            {
                return passwordDialog.EnteredPassword == FACULTY_PASSWORD;
            }
            return false;
        }

        private void LoadExams()
        {
            try
            {
                var examFiles = Directory.GetFiles(examsDirectory, "*.json");
                availableExams = examFiles.Select(Path.GetFileNameWithoutExtension).ToList();

                ExamComboBox.ItemsSource = null;
                ExamComboBox.ItemsSource = availableExams;

                if (availableExams.Count > 0)
                {
                    ExamComboBox.SelectedIndex = 0;
                }

                StatusText.Text = $"✓ Loaded {availableExams.Count} exam(s)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading exams: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateExamButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Select Exam JSON File"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string destFile = Path.Combine(examsDirectory, Path.GetFileName(dialog.FileName));
                    File.Copy(dialog.FileName, destFile, true);
                    LoadExams();
                    MessageBox.Show("Exam imported successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing exam: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GenerateCredentialsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ExamComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select an exam first.", "No Exam Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string examId = ExamComboBox.SelectedItem.ToString();
                string labId = LabIdBox.Text.Trim().ToUpper();
                string studentListText = StudentListBox.Text.Trim();

                if (string.IsNullOrEmpty(labId))
                {
                    MessageBox.Show("Please enter a Lab ID.", "Missing Lab ID",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(studentListText))
                {
                    MessageBox.Show("Please enter student IDs (one per line).", "Missing Students",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var studentIds = studentListText.Split(new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().ToUpper())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (studentIds.Count == 0)
                {
                    MessageBox.Show("No valid student IDs found.", "Invalid Input",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                currentBatchCredentials = new List<StudentCredentialInfo>();
                var existingCreds = LoadExistingCredentials();

                foreach (var studentId in studentIds)
                {
                    string loginCode = GenerateLoginCode();

                    var credential = new LabCredentialManager.StudentCredential
                    {
                        StudentId = studentId,
                        StudentName = studentId,
                        LoginCode = loginCode,
                        ExamId = examId,
                        LabId = labId,
                        CreatedAt = DateTime.Now,
                        ExpiresAt = DateTime.Now.AddHours(24),
                        IsUsed = false,
                        DeviceId = null,
                        UsedAt = null
                    };

                    existingCreds.Add(credential);

                    currentBatchCredentials.Add(new StudentCredentialInfo
                    {
                        StudentId = studentId,
                        LoginCode = loginCode,
                        ExamId = examId,
                        LabId = labId,
                        StudentName = studentId
                    });
                }

                SaveCredentials(existingCreds);

                CredentialsGrid.ItemsSource = null;
                CredentialsGrid.ItemsSource = currentBatchCredentials;

                StatusText.Text = $"✓ Generated {currentBatchCredentials.Count} credentials for {labId}";
                MessageBox.Show($"Successfully generated credentials for {currentBatchCredentials.Count} students!",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating credentials: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GenerateLoginCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private List<LabCredentialManager.StudentCredential> LoadExistingCredentials()
        {
            try
            {
                string credPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Credentials",
                    "lab_credentials.json"
                );

                if (File.Exists(credPath))
                {
                    string json = File.ReadAllText(credPath);
                    return JsonSerializer.Deserialize<List<LabCredentialManager.StudentCredential>>(json)
                        ?? new List<LabCredentialManager.StudentCredential>();
                }
            }
            catch { }

            return new List<LabCredentialManager.StudentCredential>();
        }

        private void SaveCredentials(List<LabCredentialManager.StudentCredential> credentials)
        {
            try
            {
                string credPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Credentials",
                    "lab_credentials.json"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(credPath));

                string json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(credPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving credentials: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportCredentialsButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentBatchCredentials == null || currentBatchCredentials.Count == 0)
            {
                MessageBox.Show("No credentials to export. Generate credentials first.",
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt",
                FileName = $"Credentials_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("StudentId,LoginCode,ExamId,LabId");

                    foreach (var cred in currentBatchCredentials)
                    {
                        sb.AppendLine($"{cred.StudentId},{cred.LoginCode},{cred.ExamId},{cred.LabId}");
                    }

                    File.WriteAllText(dialog.FileName, sb.ToString());
                    StatusText.Text = "✓ Credentials exported successfully";
                    MessageBox.Show("Credentials exported successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PrintCredentialsButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentBatchCredentials == null || currentBatchCredentials.Count == 0)
            {
                MessageBox.Show("No credentials to print. Generate credentials first.",
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var printDialog = new System.Windows.Controls.PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    MessageBox.Show("Print functionality - Use Export to CSV and print from Excel.",
                        "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Print error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ViewSubmissionsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string submissionsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Submissions"
                );

                if (!Directory.Exists(submissionsPath))
                {
                    MessageBox.Show("No submissions found yet.", "No Data",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var submissionFiles = Directory.GetFiles(submissionsPath, "*.json");

                if (submissionFiles.Length == 0)
                {
                    MessageBox.Show("No submissions found yet.", "No Data",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var submissions = new List<ExamSubmission>();
                foreach (var file in submissionFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var submission = JsonSerializer.Deserialize<ExamSubmission>(json);
                        if (submission != null)
                        {
                            submissions.Add(submission);
                        }
                    }
                    catch { }
                }

                if (submissions.Count == 0)
                {
                    MessageBox.Show("No valid submissions found.", "No Data",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var submissionWindow = new SubmissionsViewerWindow(submissions);
                submissionWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading submissions: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadExams();
            CredentialsGrid.ItemsSource = null;
            currentBatchCredentials = new List<StudentCredentialInfo>();
            StatusText.Text = "🔄 Refreshed";
        }
    }

    public class PasswordDialog : Window
    {
        public string EnteredPassword { get; private set; }
        private System.Windows.Controls.PasswordBox passwordBox;

        public PasswordDialog()
        {
            Title = "Faculty Authentication";
            Width = 400;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(26, 26, 26));

            var stack = new StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(new TextBlock
            {
                Text = "Enter Faculty Password:",
                FontSize = 14,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            });

            passwordBox = new System.Windows.Controls.PasswordBox
            {
                FontSize = 16,
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 0, 20)
            };
            passwordBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    EnteredPassword = passwordBox.Password;
                    DialogResult = true;
                    Close();
                }
            };
            stack.Children.Add(passwordBox);

            var btnStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okBtn = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0)
            };
            okBtn.Click += (s, e) =>
            {
                EnteredPassword = passwordBox.Password;
                DialogResult = true;
                Close();
            };
            btnStack.Children.Add(okBtn);

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30
            };
            cancelBtn.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };
            btnStack.Children.Add(cancelBtn);

            stack.Children.Add(btnStack);
            Content = stack;

            Loaded += (s, e) => passwordBox.Focus();
        }
    }

    public class SubmissionsViewerWindow : Window
    {
        public SubmissionsViewerWindow(List<ExamSubmission> submissions)
        {
            Title = "Exam Submissions";
            Width = 900;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(26, 26, 26));

            var mainGrid = new Grid { Margin = new Thickness(10) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = "Exam Submissions",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 20)
            };
            Grid.SetRow(titleBlock, 0);
            mainGrid.Children.Add(titleBlock);

            var grid = new DataGrid
            {
                ItemsSource = submissions,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(44, 44, 44)),
                Foreground = System.Windows.Media.Brushes.White,
                RowBackground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(44, 44, 44)),
                AlternatingRowBackground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(51, 51, 51))
            };

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Student ID",
                Binding = new System.Windows.Data.Binding("StudentId"),
                Width = new DataGridLength(150)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Exam ID",
                Binding = new System.Windows.Data.Binding("ExamId"),
                Width = new DataGridLength(150)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Lab ID",
                Binding = new System.Windows.Data.Binding("LabId"),
                Width = new DataGridLength(100)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Submitted At",
                Binding = new System.Windows.Data.Binding("SubmittedAt"),
                Width = new DataGridLength(200)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Time Taken (min)",
                Binding = new System.Windows.Data.Binding("TimeTaken"),
                Width = new DataGridLength(120)
            });

            Grid.SetRow(grid, 1);
            mainGrid.Children.Add(grid);

            var closeBtn = new Button
            {
                Content = "Close",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetRow(closeBtn, 2);
            mainGrid.Children.Add(closeBtn);

            Content = mainGrid;
        }
    }
}
