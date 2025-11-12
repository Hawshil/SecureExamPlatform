using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SecureExamPlatform.Core;
using SecureExamPlatform.Models;
using SecureExamPlatform.Security;

namespace SecureExamPlatform.UI
{
    public partial class SecureExamWindow : Window
    {
        private string _studentId;
        private string _examId;
        private string _labId;
        private ExamContent _examContent;
        private Dictionary<int, string> _studentAnswers;
        private DispatcherTimer _examTimer;
        private DateTime _examStartTime;
        private DateTime _examEndTime;
        private int _examDurationMinutes;
        private CalculatorWindow _calculatorWindow;
        private EnhancedProcessMonitor _processMonitor;
        private ScreenshotPrevention _screenshotPrevention;
        //private ScreenCaptureBlocker _captureBlocker;
        private const string EXIT_PASSWORD = "EXIT2025";

        public SecureExamWindow()
        {
            InitializeComponent();

            _studentAnswers = new Dictionary<int, string>();

            // Force fullscreen and block resizing
            this.WindowState = WindowState.Maximized;
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.Topmost = true;

            // Disable all text selection in the window
            DisableTextSelection(this);

            // Add keyboard event handlers
            this.PreviewKeyDown += SecureExamWindow_PreviewKeyDown;
            this.Loaded += SecureExamWindow_Loaded;
            this.Closing += SecureExamWindow_Closing;
        }

        private async void SecureExamWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(100);

            // Start security monitoring
            InitializeSecurity();
        }

        public async Task<bool> InitializeExam(string studentId, string loginCode, string examId, string labId)
        {
            try
            {
                _studentId = studentId;
                _examId = examId;
                _labId = labId;

                // Update UI
                StudentInfoText.Text = $"Student: {_studentId} | Lab: {_labId}";

                // Load exam content
                string examPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Exams",
                    $"{_examId}.json"
                );

                if (!File.Exists(examPath))
                {
                    MessageBox.Show($"Exam file not found: {_examId}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                string json = await File.ReadAllTextAsync(examPath);
                _examContent = JsonSerializer.Deserialize<ExamContent>(json);

                if (_examContent == null)
                {
                    MessageBox.Show("Failed to load exam content.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Set exam title
                ExamTitleText.Text = _examContent.Title;

                // Set exam duration
                _examDurationMinutes = _examContent.DurationMinutes;
                _examStartTime = DateTime.Now;
                _examEndTime = _examStartTime.AddMinutes(_examDurationMinutes);

                // Create session
                var credManager = new LabCredentialManager();
                credManager.CreateActiveSession(_studentId);

                // Load previous answers if resuming
                await LoadPreviousAnswers();

                // Display questions
                DisplayQuestions();

                // Start timer
                StartExamTimer();

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing exam: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogError($"Exam initialization failed: {ex}");
                return false;
            }
        }

        private void InitializeSecurity()
        {
            try
            {
                // Initialize process monitor
                _processMonitor = new EnhancedProcessMonitor();
                _processMonitor.StartMonitoring();

                // Initialize screenshot prevention (includes capture blocking)
                _screenshotPrevention = new ScreenshotPrevention();
                _screenshotPrevention.StartProtection();

                // Get window handle and protect it
                var windowHelper = new System.Windows.Interop.WindowInteropHelper(this);
                if (windowHelper.Handle != IntPtr.Zero)
                {
                    _screenshotPrevention.ProtectWindow(windowHelper.Handle);
                }
                else
                {
                    // If handle not ready, wait for window to load
                    this.SourceInitialized += (s, e) =>
                    {
                        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                        _screenshotPrevention.ProtectWindow(handle);
                    };
                }

                LogEvent("Security features activated");
            }
            catch (Exception ex)
            {
                LogError($"Security initialization error: {ex.Message}");
            }
        }



        private void DisplayQuestions()
        {
            QuestionsPanel.Children.Clear();

            if (_examContent?.Questions == null || _examContent.Questions.Count == 0)
            {
                var errorText = new TextBlock
                {
                    Text = "No questions available in this exam.",
                    Foreground = Brushes.Red,
                    FontSize = 16,
                    Margin = new Thickness(0, 20, 0, 0)
                };
                QuestionsPanel.Children.Add(errorText);
                return;
            }

            for (int i = 0; i < _examContent.Questions.Count; i++)
            {
                var question = _examContent.Questions[i];
                var questionPanel = CreateQuestionPanel(question, i);
                QuestionsPanel.Children.Add(questionPanel);
            }

            UpdateProgressText();
        }

        private Border CreateQuestionPanel(Question question, int index)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(44, 44, 44)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(25),
                Margin = new Thickness(0, 0, 0, 20)
            };

            var mainStack = new StackPanel();

            // Question number and text (non-selectable)
            var questionHeader = new TextBlock
            {
                Text = $"Question {index + 1}",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                Margin = new Thickness(0, 0, 0, 10),
                IsHitTestVisible = false,
                Focusable = false
            };
            mainStack.Children.Add(questionHeader);

            // Obfuscated question text
            var questionText = new TextBlock
            {
                Text = ObfuscateText(question.Text),
                FontSize = 16,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20),
                IsHitTestVisible = false,
                Focusable = false
            };
            mainStack.Children.Add(questionText);

            // Answer options
            if (question.Type == "MCQ" && question.Options != null)
            {
                var optionsPanel = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };

                for (int i = 0; i < question.Options.Count; i++)
                {
                    var radio = new RadioButton
                    {
                        Content = ObfuscateText(question.Options[i]),
                        GroupName = $"Question_{index}",
                        Foreground = Brushes.White,
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 0, 10),
                        Tag = $"{index}_{i}",
                        IsChecked = _studentAnswers.ContainsKey(index) && _studentAnswers[index] == question.Options[i]
                    };
                    radio.Checked += RadioButton_Checked;

                    // Make content non-selectable
                    if (radio.Content is string)
                    {
                        var textBlock = new TextBlock
                        {
                            Text = (string)radio.Content,
                            IsHitTestVisible = false,
                            Focusable = false
                        };
                        radio.Content = textBlock;
                    }

                    optionsPanel.Children.Add(radio);
                }

                mainStack.Children.Add(optionsPanel);
            }
            else if (question.Type == "SHORT")
            {
                var answerBox = new TextBox
                {
                    Height = 40,
                    Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)),
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(10),
                    FontSize = 14,
                    Tag = index,
                    Text = _studentAnswers.ContainsKey(index) ? _studentAnswers[index] : ""
                };
                answerBox.TextChanged += AnswerBox_TextChanged;
                mainStack.Children.Add(answerBox);
            }
            else if (question.Type == "LONG")
            {
                var answerBox = new TextBox
                {
                    Height = 120,
                    Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)),
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(10),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Tag = index,
                    Text = _studentAnswers.ContainsKey(index) ? _studentAnswers[index] : ""
                };
                answerBox.TextChanged += AnswerBox_TextChanged;
                mainStack.Children.Add(answerBox);
            }

            border.Child = mainStack;
            return border;
        }

        private string ObfuscateText(string text)
        {
            // Add zero-width characters to make text unreadable when copied
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                sb.Append(c);
                sb.Append('\u200B'); // Zero-width space
            }
            return sb.ToString();
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            var radio = sender as RadioButton;
            if (radio != null && radio.Tag != null)
            {
                var parts = radio.Tag.ToString().Split('_');
                int questionIndex = int.Parse(parts[0]);
                int optionIndex = int.Parse(parts[1]);

                var question = _examContent.Questions[questionIndex];
                _studentAnswers[questionIndex] = question.Options[optionIndex];

                SaveAnswers();
                UpdateProgressText();
            }
        }

        private void AnswerBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null && textBox.Tag != null)
            {
                int questionIndex = (int)textBox.Tag;
                _studentAnswers[questionIndex] = textBox.Text;

                SaveAnswers();
                UpdateProgressText();
            }
        }

        private void StartExamTimer()
        {
            _examTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _examTimer.Tick += ExamTimer_Tick;
            _examTimer.Start();
        }

        private void ExamTimer_Tick(object sender, EventArgs e)
        {
            TimeSpan remaining = _examEndTime - DateTime.Now;

            if (remaining.TotalSeconds <= 0)
            {
                _examTimer.Stop();
                TimerText.Text = "00:00:00";
                AutoSubmitExam();
                return;
            }

            TimerText.Text = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

            // Change color when less than 5 minutes
            if (remaining.TotalMinutes <= 5)
            {
                TimerText.Foreground = Brushes.Yellow;
            }
        }

        private void UpdateProgressText()
        {
            int answered = _studentAnswers.Count;
            int total = _examContent?.Questions?.Count ?? 0;
            ProgressText.Text = $"Progress: {answered}/{total} answered";
        }

        private async void SaveAnswers()
        {
            try
            {
                string sessionPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Student",
                    "Sessions",
                    $"{_studentId}_{_examId}_session.json"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(sessionPath));

                var sessionData = new
                {
                    StudentId = _studentId,
                    ExamId = _examId,
                    Answers = _studentAnswers,
                    LastSaved = DateTime.Now
                };

                string json = JsonSerializer.Serialize(sessionData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(sessionPath, json);
            }
            catch (Exception ex)
            {
                LogError($"Error saving answers: {ex.Message}");
            }
        }

        private async Task LoadPreviousAnswers()
        {
            try
            {
                string sessionPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Student",
                    "Sessions",
                    $"{_studentId}_{_examId}_session.json"
                );

                if (File.Exists(sessionPath))
                {
                    string json = await File.ReadAllTextAsync(sessionPath);
                    var sessionData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                    if (sessionData != null && sessionData.ContainsKey("Answers"))
                    {
                        var answersElement = (JsonElement)sessionData["Answers"];
                        foreach (var prop in answersElement.EnumerateObject())
                        {
                            if (int.TryParse(prop.Name, out int index))
                            {
                                _studentAnswers[index] = prop.Value.GetString();
                            }
                        }

                        StatusText.Text = "📝 Previous session restored";
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error loading previous answers: {ex.Message}");
            }
        }

        private void CalculatorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_calculatorWindow == null || !_calculatorWindow.IsVisible)
            {
                _calculatorWindow = new CalculatorWindow();
                _calculatorWindow.Show();
            }
            else
            {
                _calculatorWindow.Activate();
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            DisplayQuestions();
            StatusText.Text = "🔄 Display refreshed";
        }

        private async void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to submit your exam?\n\n" +
                $"You have answered {_studentAnswers.Count} out of {_examContent.Questions.Count} questions.\n\n" +
                $"This action cannot be undone!",
                "Confirm Submission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Double confirmation
                var confirmResult = MessageBox.Show(
                    "Final Confirmation: Submit your exam now?",
                    "Final Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmResult == MessageBoxResult.Yes)
                {
                    await SubmitExam();
                }
            }
        }

        private async Task SubmitExam()
        {
            try
            {
                BlockingOverlay.Visibility = Visibility.Visible;
                SubmitButton.IsEnabled = false;

                // Create submission
                var submission = new ExamSubmission
                {
                    StudentId = _studentId,
                    ExamId = _examId,
                    LabId = _labId,
                    Answers = _studentAnswers,
                    SubmittedAt = DateTime.Now,
                    TimeTaken = (int)(DateTime.Now - _examStartTime).TotalMinutes
                };

                string submissionPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Submissions",
                    $"{_studentId}_{_examId}_{DateTime.Now:yyyyMMddHHmmss}.json"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(submissionPath));

                string json = JsonSerializer.Serialize(submission, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(submissionPath, json);

                // Delete session file
                string sessionPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Student",
                    "Sessions",
                    $"{_studentId}_{_examId}_session.json"
                );
                if (File.Exists(sessionPath))
                {
                    File.Delete(sessionPath);
                }

                LogEvent($"Exam submitted successfully by {_studentId}");

                MessageBox.Show(
                    "✓ Your exam has been submitted successfully!\n\n" +
                    "Thank you for taking the exam.\n" +
                    "You can now close this window.",
                    "Submission Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Cleanup and exit
                CleanupSecurity();
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                BlockingOverlay.Visibility = Visibility.Collapsed;
                SubmitButton.IsEnabled = true;

                MessageBox.Show(
                    $"Error submitting exam: {ex.Message}\n\nPlease contact your invigilator.",
                    "Submission Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                LogError($"Submission error: {ex}");
            }
        }

        private async void AutoSubmitExam()
        {
            MessageBox.Show(
                "⏰ Time is up! Your exam will be submitted automatically.",
                "Time's Up",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await SubmitExam();
        }

        private void SecureExamWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Alt+E for Emergency Exit (with password)
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.E)
            {
                PromptExitWithPassword();
                e.Handled = true;
                return;
            }

            // Alt+R for Refresh
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.R)
            {
                RefreshButton_Click(sender, e);
                e.Handled = true;
                return;
            }

            // Block most keyboard shortcuts
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                if (e.Key == Key.F4 || e.Key == Key.Tab)
                {
                    e.Handled = true;
                    return;
                }
            }

            // Block Windows key
            if (e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
                return;
            }

            // Block Ctrl+C, Ctrl+V, Ctrl+X (copy/paste)
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.C || e.Key == Key.V || e.Key == Key.X)
                {
                    // Allow in textboxes only
                    if (!(Keyboard.FocusedElement is TextBox))
                    {
                        e.Handled = true;
                        return;
                    }
                }
            }

            // Block PrintScreen
            if (e.Key == Key.PrintScreen || e.Key == Key.Snapshot)
            {
                e.Handled = true;
                return;
            }

            // Block Alt+PrintScreen
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.PrintScreen)
            {
                e.Handled = true;
                return;
            }
        }

        private void PromptExitWithPassword()
        {
            var passwordDialog = new ExitPasswordDialog();
            if (passwordDialog.ShowDialog() == true)
            {
                if (passwordDialog.EnteredPassword == EXIT_PASSWORD)
                {
                    var confirm = MessageBox.Show(
                        "Exit without submitting? Your progress will be saved.",
                        "Confirm Exit",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirm == MessageBoxResult.Yes)
                    {
                        SaveAnswers();
                        CleanupSecurity();
                        Application.Current.Shutdown();
                    }
                }
                else
                {
                    MessageBox.Show(
                        "Incorrect exit password.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private void DisableTextSelection(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TextBlock textBlock)
                {
                    textBlock.IsHitTestVisible = false;
                    textBlock.Focusable = false;
                }

                DisableTextSelection(child);
            }
        }

        private void SecureExamWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Prevent closing without password
            e.Cancel = true;
            PromptExitWithPassword();
        }

        private void CleanupSecurity()
        {
            try
            {
                _processMonitor?.StopMonitoring();
                _screenshotPrevention?.StopProtection();
                _screenshotPrevention?.Dispose();
                _calculatorWindow?.Close();
                _examTimer?.Stop();
            }
            catch { }
        }


        private void LogEvent(string message)
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Student", "Logs", $"exam_{DateTime.Now:yyyyMMdd}.log"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        private void LogError(string message)
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Student", "Logs", $"errors_{DateTime.Now:yyyyMMdd}.log"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}\n");
            }
            catch { }
        }
    }
}
