using SecureExam.Core;
using SecureExam.Core.Core;
using SecureExam.Core.Models;
using SecureExam.Core.Security;
using SecureExam.Security;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SecureExam.Student
{
    public partial class SecureExamWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        private readonly string studentId;
        private readonly string examId;
        private readonly string labId;
        private readonly ExamContent examContent;
        private readonly Dictionary<int, string> answers;
        private readonly ExamSessionManager sessionManager;
        private readonly DispatcherTimer examTimer;
        private readonly DispatcherTimer securityTimer;
        private readonly EnhancedProcessMonitor processMonitor;
        private readonly ScreenshotPrevention screenshotPrevention;
        private DateTime examStartTime;
        private TimeSpan remainingTime;
        private bool _isProperExit = false;
        private bool _isSubmitting = false;
        private int currentQuestionIndex = 0;
        private CalculatorWindow calculatorWindow;

        public SecureExamWindow(string studentId, string examId, string labId, ExamContent exam)
        {
            InitializeComponent();
            this.studentId = studentId;
            this.examId = examId;
            this.labId = labId;
            this.examContent = exam;
            this.answers = new Dictionary<int, string>();

            sessionManager = new ExamSessionManager();
            processMonitor = new EnhancedProcessMonitor();
            screenshotPrevention = new ScreenshotPrevention();

            // Initialize security FIRST
            InitializeSecurity();

            // Get window handle and protect it
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                screenshotPrevention.ProtectWindow(hwnd);
            }

            // Start protection systems
            screenshotPrevention.StartProtection();
            processMonitor.StartMonitoring();

            // Load saved answers if exist
            LoadSavedAnswers();

            // Create session
            string deviceId = Environment.MachineName;
            sessionManager.CreateSession(studentId, examId, deviceId);

            // Setup exam
            examStartTime = DateTime.Now;
            remainingTime = TimeSpan.FromMinutes(exam.DurationMinutes);
            ExamTitleText.Text = exam.Title;
            StudentInfoText.Text = $"Student: {studentId} | Exam: {examId} | Lab: {labId}";

            // Setup exam timer
            examTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            examTimer.Tick += ExamTimer_Tick;
            examTimer.Start();

            // Security monitoring timer
            securityTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            securityTimer.Tick += SecurityTimer_Tick;
            securityTimer.Start();

            // Load questions navigation
            LoadQuestionNavigation();
            DisplayQuestion(0);

            UpdateProgress();
        }

        private void InitializeSecurity()
        {
            // Block all dangerous keyboard shortcuts
            this.PreviewKeyDown += SecureWindow_PreviewKeyDown;
            this.Closing += SecureWindow_Closing;

            // Force topmost and maximized
            var topmostTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            topmostTimer.Tick += (s, e) =>
            {
                this.Topmost = true;
                this.WindowState = WindowState.Maximized;
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            };
            topmostTimer.Start();

            // Disable clipboard copying
            DataObject.AddCopyingHandler(this, (s, e) => e.CancelCommand());
        }

        private void SecureWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Block Alt+F4 - Require password
            if (e.Key == Key.System && e.SystemKey == Key.F4)
            {
                e.Handled = true;
                HandleExitAttempt();
                return;
            }

            // Block Windows key
            if (e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
                return;
            }

            // Block dangerous Ctrl combinations
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.Escape || e.Key == Key.Tab || e.Key == Key.W ||
                    e.Key == Key.N || e.Key == Key.T)
                {
                    e.Handled = true;
                    return;
                }
            }

            // Block Alt+Tab
            if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                e.Handled = true;
                return;
            }

            // ALLOW Alt+R for refresh
            if (e.Key == Key.R && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                e.Handled = true;
                RefreshButton_Click(null, null);
                return;
            }

            // ALLOW Alt+E for emergency exit
            if (e.Key == Key.E && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                e.Handled = true;
                HandleExitAttempt();
                return;
            }

            // Block F-keys except F5
            if (e.Key >= Key.F1 && e.Key <= Key.F12 && e.Key != Key.F5)
            {
                e.Handled = true;
                return;
            }

            // Block PrintScreen
            if (e.Key == Key.PrintScreen || e.Key == Key.Snapshot)
            {
                e.Handled = true;
                return;
            }
        }

        private void SecureWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isProperExit && !_isSubmitting)
            {
                e.Cancel = true;
                MessageBox.Show(
                    "Use Alt+E or the Exit button with the exit password to close the exam.\n\nExit Password is provided by your invigilator.",
                    "Cannot Close",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void HandleExitAttempt()
        {
            var result = MessageBox.Show(
                "Are you sure you want to exit the exam?\n\nYour answers will be saved but you'll need to contact your instructor.",
                "Confirm Exit",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var dialog = new ExitPasswordDialog();
                if (dialog.ShowDialog() == true)
                {
                    if (dialog.EnteredPassword == "EXIT2025")
                    {
                        SaveAnswers(false);
                        _isProperExit = true;
                        Cleanup();
                        Application.Current.Shutdown();
                    }
                    else
                    {
                        MessageBox.Show("Incorrect exit password!", "Access Denied",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void SecurityTimer_Tick(object sender, EventArgs e)
        {
            this.Topmost = true;
            this.WindowState = WindowState.Maximized;
            this.Focus();
        }

        private void LoadQuestionNavigation()
        {
            QuestionNavPanel.Children.Clear();

            for (int i = 0; i < examContent.Questions.Count; i++)
            {
                int index = i; // Capture for closure
                var button = new Button
                {
                    Content = $"Q{i + 1}",
                    Height = 40,
                    Margin = new Thickness(5),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Background = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Tag = index
                };

                button.Click += (s, e) =>
                {
                    DisplayQuestion((int)((Button)s).Tag);
                };

                QuestionNavPanel.Children.Add(button);
            }

            UpdateNavigationButtons();
        }

        private void UpdateNavigationButtons()
        {
            // Update question navigation button colors
            foreach (var child in QuestionNavPanel.Children)
            {
                if (child is Button btn)
                {
                    int index = (int)btn.Tag;

                    if (index == currentQuestionIndex)
                    {
                        // Current question - Blue
                        btn.Background = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                    }
                    else if (answers.ContainsKey(index) && !string.IsNullOrWhiteSpace(answers[index]))
                    {
                        // Answered - Green
                        btn.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                    }
                    else
                    {
                        // Unanswered - Gray
                        btn.Background = new SolidColorBrush(Color.FromRgb(68, 68, 68));
                    }
                }
            }

            // Update Previous/Next buttons
            PreviousButton.IsEnabled = currentQuestionIndex > 0;
            NextButton.Content = currentQuestionIndex < examContent.Questions.Count - 1 ? "Next →" : "Last Question";
            NextButton.IsEnabled = currentQuestionIndex < examContent.Questions.Count - 1;
        }

        private void DisplayQuestion(int index)
        {
            if (index < 0 || index >= examContent.Questions.Count)
                return;

            currentQuestionIndex = index;
            CurrentQuestionPanel.Children.Clear();

            var question = examContent.Questions[index];

            // Question header
            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 20)
            };

            headerStack.Children.Add(new TextBlock
            {
                Text = $"Question {index + 1}",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243))
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = $" ({question.Points} pts)",
                FontSize = 18,
                Foreground = Brushes.Gray,
                Margin = new Thickness(10, 0, 0, 0)
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = $" - {question.Type}",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                Margin = new Thickness(15, 0, 0, 0)
            });

            CurrentQuestionPanel.Children.Add(headerStack);

            // Question text
            var questionText = new TextBlock
            {
                Text = question.Text,
                FontSize = 18,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 30),
                LineHeight = 28
            };
            CurrentQuestionPanel.Children.Add(questionText);

            // Answer input based on question type
            if (question.Type == "MCQ" && question.Options != null)
            {
                var optionsStack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

                for (int i = 0; i < question.Options.Count; i++)
                {
                    var radio = new RadioButton
                    {
                        Content = question.Options[i],
                        FontSize = 16,
                        Foreground = Brushes.White,
                        Margin = new Thickness(0, 0, 0, 15),
                        GroupName = $"Q{index}",
                        Tag = index,
                        Padding = new Thickness(10)
                    };

                    radio.Checked += (s, e) =>
                    {
                        int qIndex = (int)((RadioButton)s).Tag;
                        answers[qIndex] = ((RadioButton)s).Content.ToString();
                        UpdateProgress();
                        UpdateNavigationButtons();
                    };

                    // Restore saved answer
                    if (answers.ContainsKey(index) && answers[index] == question.Options[i])
                    {
                        radio.IsChecked = true;
                    }

                    optionsStack.Children.Add(radio);
                }

                CurrentQuestionPanel.Children.Add(optionsStack);
            }
            else if (question.Type == "SHORT")
            {
                var textBox = new TextBox
                {
                    Height = 100,
                    FontSize = 16,
                    Padding = new Thickness(15),
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Tag = index,
                    Text = answers.ContainsKey(index) ? answers[index] : ""
                };

                textBox.TextChanged += (s, e) =>
                {
                    int qIndex = (int)((TextBox)s).Tag;
                    answers[qIndex] = ((TextBox)s).Text;
                    UpdateProgress();
                    UpdateNavigationButtons();
                };

                CurrentQuestionPanel.Children.Add(textBox);
            }
            else if (question.Type == "LONG")
            {
                var textBox = new TextBox
                {
                    Height = 250,
                    FontSize = 16,
                    Padding = new Thickness(15),
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Tag = index,
                    Text = answers.ContainsKey(index) ? answers[index] : ""
                };

                textBox.TextChanged += (s, e) =>
                {
                    int qIndex = (int)((TextBox)s).Tag;
                    answers[qIndex] = ((TextBox)s).Text;
                    UpdateProgress();
                    UpdateNavigationButtons();
                };

                CurrentQuestionPanel.Children.Add(textBox);
            }

            UpdateNavigationButtons();
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentQuestionIndex > 0)
            {
                DisplayQuestion(currentQuestionIndex - 1);
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentQuestionIndex < examContent.Questions.Count - 1)
            {
                DisplayQuestion(currentQuestionIndex + 1);
            }
        }

        private void ExamTimer_Tick(object sender, EventArgs e)
        {
            remainingTime = remainingTime.Subtract(TimeSpan.FromSeconds(1));

            if (remainingTime.TotalSeconds <= 0)
            {
                examTimer.Stop();
                TimerText.Text = "TIME UP!";
                TimerText.Foreground = Brushes.Red;
                AutoSubmit();
            }
            else
            {
                TimerText.Text = $"{(int)remainingTime.TotalHours:D2}:{remainingTime.Minutes:D2}:{remainingTime.Seconds:D2}";

                // Warning colors
                if (remainingTime.TotalMinutes <= 5)
                {
                    TimerText.Foreground = Brushes.Red;
                }
                else if (remainingTime.TotalMinutes <= 10)
                {
                    TimerText.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                }
            }
        }

        private void UpdateProgress()
        {
            int answered = answers.Count(a => !string.IsNullOrWhiteSpace(a.Value));
            ProgressText.Text = $"{answered}/{examContent.Questions.Count} answered";
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            SaveAnswers(false);
            StatusText.Text = "✓ Answers saved";
            Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = "📝 Continue answering";
            }, DispatcherPriority.Background);
        }

        private void CalculatorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (calculatorWindow == null)
                {
                    calculatorWindow = new CalculatorWindow
                    {
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.Manual
                    };

                    // Position calculator on the right side of the screen
                    calculatorWindow.Left = this.Left + this.ActualWidth - calculatorWindow.Width - 50;
                    calculatorWindow.Top = this.Top + 100;
                }

                if (calculatorWindow.Visibility == Visibility.Visible)
                {
                    calculatorWindow.Hide();
                    CalculatorButton.Background = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                }
                else
                {
                    calculatorWindow.Show();
                    calculatorWindow.Topmost = true;
                    CalculatorButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Calculator error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        ////private void CloseCalculatorButton_Click(object sender, RoutedEventArgs e)
        //{
        //    CalculatorPanel.Visibility = Visibility.Collapsed;
        //}

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to submit?\n\nAnswered: {answers.Count}/{examContent.Questions.Count} questions\nTime remaining: {TimerText.Text}",
                "Confirm Submission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SaveAnswers(true);
            }
        }

        private void AutoSubmit()
        {
            MessageBox.Show("Time is up! Your exam will be submitted automatically.", "Time Up",
                MessageBoxButton.OK, MessageBoxImage.Information);
            SaveAnswers(true);
        }

        private void SaveAnswers(bool isSubmission)
        {
            try
            {
                _isSubmitting = isSubmission;
                BlockingOverlay.Visibility = Visibility.Visible;
                SubmitButton.IsEnabled = false;

                if (isSubmission)
                {
                    // Save final submission
                    var submission = new ExamSubmission
                    {
                        StudentId = studentId,
                        ExamId = examId,
                        LabId = labId,
                        Answers = answers,
                        SubmittedAt = DateTime.Now,
                        TimeTaken = (int)(DateTime.Now - examStartTime).TotalMinutes
                    };

                    string submissionsDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SecureExam",
                        "Submissions"
                    );
                    Directory.CreateDirectory(submissionsDir);

                    string filename = $"{studentId}_{examId}_{DateTime.Now:yyyyMMddHHmmss}.json";
                    string filepath = Path.Combine(submissionsDir, filename);

                    string json = JsonSerializer.Serialize(submission, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(filepath, json);

                    MessageBox.Show(
                        $"Exam submitted successfully!\n\nStudent: {studentId}\nSubmission ID: {filename}",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    _isProperExit = true;
                    Cleanup();
                    Application.Current.Shutdown();
                }
                else
                {
                    // Save in-progress answers
                    sessionManager.SaveAnswers(answers);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (!isSubmission)
                {
                    BlockingOverlay.Visibility = Visibility.Collapsed;
                    SubmitButton.IsEnabled = true;
                }
            }
        }

        private void LoadSavedAnswers()
        {
            try
            {
                var saved = sessionManager.LoadSavedAnswers(studentId, examId);
                if (saved != null && saved.Count > 0)
                {
                    foreach (var kvp in saved)
                    {
                        answers[kvp.Key] = kvp.Value;
                    }

                    var result = MessageBox.Show(
                        "Found saved answers from a previous session. Do you want to restore them?",
                        "Restore Progress",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.No)
                    {
                        answers.Clear();
                    }
                }
            }
            catch { }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            HandleExitAttempt();
        }

        private void Cleanup()
        {
            try
            {
                examTimer?.Stop();
                securityTimer?.Stop();
                processMonitor?.StopMonitoring();
                screenshotPrevention?.StopProtection();

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    screenshotPrevention?.UnprotectWindow(hwnd);
                }

                sessionManager?.EndSession();
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            Cleanup();
            base.OnClosed(e);
        }
    }
}
