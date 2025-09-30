using SecureExamPlatform.Core;
using SecureExamPlatform.Models;
using SecureExamPlatform.Security;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SecureExamPlatform.UI
{
    /// <summary>
    /// Main exam window that runs in kiosk mode
    /// </summary>
    public partial class ExamWindow : Window, INotifyPropertyChanged
    {
        // Core components
        private readonly ExamSessionManager _sessionManager;
        private readonly EnhancedProcessMonitor _processMonitor;
        private readonly ScreenshotPrevention _screenshotPrevention;
        private readonly ScreenCaptureBlocker _captureBlocker;

        // Exam state
        private ExamContent _examContent;
        private Dictionary<string, string> _studentAnswers;
        private int _currentQuestionIndex;
        private Question _currentQuestion;

        // Timers
        private readonly DispatcherTimer _examTimer;
        private readonly DispatcherTimer _autoSaveTimer;
        private TimeSpan _remainingTime;

        // Windows API imports for kiosk mode
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;

        public ExamWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Initialize components
            _sessionManager = new ExamSessionManager();
            _processMonitor = new EnhancedProcessMonitor();
            _screenshotPrevention = new ScreenshotPrevention();
            _captureBlocker = new ScreenCaptureBlocker();

            _studentAnswers = new Dictionary<string, string>();

            // Setup timers
            _examTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _examTimer.Tick += UpdateExamTimer;

            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _autoSaveTimer.Tick += AutoSaveAnswers;

            // Configure window for kiosk mode
            ConfigureKioskMode();

            // Start security monitoring
            StartSecurityMonitoring();
        }

        private void ConfigureKioskMode()
        {
            // Set window style for kiosk mode
            this.WindowStyle = WindowStyle.None;
            this.WindowState = WindowState.Maximized;
            this.ResizeMode = ResizeMode.NoResize;
            this.Topmost = true;

            // Prevent Alt+F4
            this.Closing += (s, e) =>
            {
                if (_examContent != null /* && _sessionManager.IsSessionActive() */) // IsSessionActive method needs to be added to ExamSessionManager
                {
                    e.Cancel = true; // Can't close during exam
                    System.Windows.MessageBox.Show("You cannot close the exam window. Please submit your exam first.",
                        "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            // Remove window chrome using WinAPI
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            int style = GetWindowLong(helper.Handle, GWL_STYLE);
            SetWindowLong(helper.Handle, GWL_STYLE, style & ~WS_SYSMENU);

            // Keep window on top
            SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

            // Capture all keyboard input
            PreviewKeyDown += OnPreviewKeyDown;

            // Prevent screenshot
            _screenshotPrevention.ProtectWindow(helper.Handle);
        }

        private void StartSecurityMonitoring()
        {
            // Start process monitoring
            _processMonitor.StartMonitoring();

            // Start screenshot blocking
            _captureBlocker.StartBlocking();
            _screenshotPrevention.StartProtection();
        }

        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Block dangerous key combinations
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                if (e.Key == Key.Tab || e.Key == Key.F4 || e.Key == Key.Escape)
                {
                    e.Handled = true;
                    LogSecurityEvent("Blocked Alt key combination");
                    return;
                }
            }

            if (Keyboard.Modifiers == ModifierKeys.Windows ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
                LogSecurityEvent("Blocked Windows key");
                return;
            }

            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) &&
                e.Key == Key.Delete)
            {
                e.Handled = true;
                LogSecurityEvent("Blocked Ctrl+Alt+Del");
                return;
            }

            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
                e.Key == Key.Escape)
            {
                e.Handled = true;
                LogSecurityEvent("Blocked Task Manager shortcut");
                return;
            }
        }

        public async Task<bool> StartExam(string studentId, string sessionToken, string examId)
        {
            try
            {
                // This method needs to be implemented fully in ExamSessionManager
                // For now, let's assume it returns a mock result.
                var result = new { Success = true, ErrorMessage = "", ExamContent = GetMockExamContent(examId) }; // MOCK
                // var result = await _sessionManager.StartSession(studentId, sessionToken, examId);

                if (!result.Success)
                {
                    System.Windows.MessageBox.Show(result.ErrorMessage, "Login Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Load exam content
                _examContent = result.ExamContent;
                _currentQuestionIndex = 0;
                _remainingTime = _examContent.Duration;

                // Display first question
                if (_examContent.Questions.Any())
                {
                    DisplayQuestion(_examContent.Questions[0]);
                }

                // Start timers
                _examTimer.Start();
                _autoSaveTimer.Start();

                // Update UI
                OnPropertyChanged(nameof(ExamTitle));
                OnPropertyChanged(nameof(StudentId));
                OnPropertyChanged(nameof(QuestionProgress));

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to start exam: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // Helper method for mock data
        private ExamContent GetMockExamContent(string examId)
        {
            return new ExamContent
            {
                ExamId = examId,
                Title = "Computer Science 101 Final Exam",
                Duration = TimeSpan.FromMinutes(60),
                Questions = new List<Question>
                {
                    new Question { Id = "Q1", Text = "What is the primary function of a compiler?" },
                    new Question { Id = "Q2", Text = "Explain the difference between a class and an object." },
                    new Question { Id = "Q3", Text = "What does the 'static' keyword mean in C#?" }
                }
            };
        }

        private void DisplayQuestion(Question question)
        {
            _currentQuestion = question;

            // Update question display
            OnPropertyChanged(nameof(CurrentQuestionText));
            OnPropertyChanged(nameof(CurrentQuestionNumber));

            // Load saved answer if exists
            if (_studentAnswers.ContainsKey(question.Id))
            {
                // Restore previous answer
                LoadSavedAnswer(question.Id);
            }
        }

        private void SaveCurrentAnswer()
        {
            if (_currentQuestion == null) return;

            // Get answer based on question type
            string answer = GetCurrentAnswer();

            if (!string.IsNullOrEmpty(answer))
            {
                _studentAnswers[_currentQuestion.Id] = answer;
            }
        }

        private string GetCurrentAnswer()
        {
            // This would get the actual answer from UI controls
            // Placeholder implementation
            return "placeholder_answer";
        }

        private void LoadSavedAnswer(string questionId)
        {
            if (_studentAnswers.ContainsKey(questionId))
            {
                // Load the answer into UI controls
                // Placeholder implementation
            }
        }

        private void NavigateToQuestion(int index)
        {
            if (_examContent == null || index < 0 || index >= _examContent.Questions.Count) return;

            // Save current answer
            SaveCurrentAnswer();

            // Navigate to new question
            _currentQuestionIndex = index;
            DisplayQuestion(_examContent.Questions[index]);
            OnPropertyChanged(nameof(QuestionProgress));
        }

        private void OnPreviousQuestion()
        {
            NavigateToQuestion(_currentQuestionIndex - 1);
        }

        private void OnNextQuestion()
        {
            NavigateToQuestion(_currentQuestionIndex + 1);
        }

        private async void OnSubmitExam()
        {
            // Confirm submission
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to submit your exam? This action cannot be undone.",
                "Confirm Submission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Save current answer
            SaveCurrentAnswer();

            // Stop timers
            _examTimer.Stop();
            _autoSaveTimer.Stop();

            // Create submission
            var submission = new ExamSubmission
            {
                //SessionId = _sessionManager.GetCurrentSession()?.SessionId, // Needs GetCurrentSession method
                //StudentId = _sessionManager.GetCurrentSession()?.StudentId,
                ExamId = _examContent.ExamId,
                Answers = _studentAnswers,
                SubmissionTime = DateTime.Now,
                TimeSpent = _examContent.Duration - _remainingTime
            };

            // Save submission
            await SaveSubmission(submission);

            // End session
            // _sessionManager.EndSession(true);

            // Show completion message
            System.Windows.MessageBox.Show(
                "Your exam has been submitted successfully.\n\n" +
                $"Submission Time: {submission.SubmissionTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Time Spent: {submission.TimeSpent.TotalMinutes:F0} minutes\n" +
                $"Questions Answered: {submission.Answers.Count}/{_examContent.Questions.Count}",
                "Exam Submitted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Clean up and close
            CleanupAndExit();
        }

        // These methods connect the XAML buttons to your C# logic.
        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            OnPreviousQuestion();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            OnNextQuestion();
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            OnSubmitExam();
        }

        private async Task SaveSubmission(ExamSubmission submission)
        {
            // Save to encrypted local file
            // In production, also send to server
            await Task.Run(() =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(submission);
                var encrypted = EncryptData(json);
                var filename = $"submission_{submission.StudentId}_{DateTime.Now:yyyyMMdd_HHmmss}.dat";
                System.IO.File.WriteAllBytes(filename, encrypted);
            });
        }

        private byte[] EncryptData(string data)
        {
            // Placeholder for encryption
            // In production, use proper AES encryption
            return System.Text.Encoding.UTF8.GetBytes(data);
        }

        private void UpdateExamTimer(object sender, EventArgs e)
        {
            _remainingTime = _remainingTime.Subtract(TimeSpan.FromSeconds(1));

            if (_remainingTime <= TimeSpan.Zero)
            {
                // Time's up - auto submit
                _examTimer.Stop();
                System.Windows.MessageBox.Show("Time is up! The exam will now be submitted automatically.", "Time's Up", MessageBoxButton.OK, MessageBoxImage.Information);
                OnSubmitExam();
                return;
            }

            OnPropertyChanged(nameof(RemainingTimeDisplay));

            // Warning at 5 minutes remaining
            if (_remainingTime.TotalSeconds > 299 && _remainingTime.TotalSeconds <= 300)
            {
                System.Windows.MessageBox.Show("You have 5 minutes remaining!", "Time Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AutoSaveAnswers(object sender, EventArgs e)
        {
            SaveCurrentAnswer();
            LogEvent("Auto-saved answers");
        }

        private void LogSecurityEvent(string message)
        {
            Console.WriteLine($"[SECURITY] {message}");
        }

        private void LogEvent(string message)
        {
            Console.WriteLine($"[EXAM] {message}");
        }

        private void CleanupAndExit()
        {
            // Stop all monitoring
            _processMonitor?.StopMonitoring();
            _screenshotPrevention?.StopProtection();
            _captureBlocker?.StopBlocking();

            // Dispose resources
            _processMonitor?.Dispose();
            _screenshotPrevention?.Dispose();

            // Close application
            System.Windows.Application.Current.Shutdown();
        }

        // Properties for data binding
        public string ExamTitle => _examContent?.Title ?? "Loading Exam...";
        public string StudentId => "S12345"; // Mock data
        public string QuestionProgress => $"Question {_currentQuestionIndex + 1} of {_examContent?.Questions.Count ?? 0}";
        public string CurrentQuestionText => _currentQuestion?.Text ?? "";
        public int CurrentQuestionNumber => _currentQuestionIndex + 1;
        public string RemainingTimeDisplay => $"{_remainingTime.Hours:D2}:{_remainingTime.Minutes:D2}:{_remainingTime.Seconds:D2}";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}