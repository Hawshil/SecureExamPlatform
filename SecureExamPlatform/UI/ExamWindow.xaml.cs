using SecureExamPlatform.Core;
using SecureExamPlatform.Models;
using SecureExamPlatform.Security;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        // UI Properties
        private string _studentId;
        private string _examTitle;
        private string _questionProgress;
        private string _remainingTimeDisplay;
        private int _currentQuestionNumber;
        private string _currentQuestionText;
        private ObservableCollection<QuestionViewModel> _questions;

        // Windows API imports for kiosk mode
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_STYLE = -16;
        private const int WS_SY_MENU = 0x80000;
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
            _questions = new ObservableCollection<QuestionViewModel>();

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

        #region Properties for Data Binding

        public string StudentId
        {
            get => _studentId;
            set
            {
                _studentId = value;
                OnPropertyChanged();
            }
        }

        public string ExamTitle
        {
            get => _examTitle;
            set
            {
                _examTitle = value;
                OnPropertyChanged();
            }
        }

        public string QuestionProgress
        {
            get => _questionProgress;
            set
            {
                _questionProgress = value;
                OnPropertyChanged();
            }
        }

        public string RemainingTimeDisplay
        {
            get => _remainingTimeDisplay;
            set
            {
                _remainingTimeDisplay = value;
                OnPropertyChanged();
            }
        }

        public int CurrentQuestionNumber
        {
            get => _currentQuestionNumber;
            set
            {
                _currentQuestionNumber = value;
                OnPropertyChanged();
            }
        }

        public string CurrentQuestionText
        {
            get => _currentQuestionText;
            set
            {
                _currentQuestionText = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<QuestionViewModel> Questions
        {
            get => _questions;
            set
            {
                _questions = value;
                OnPropertyChanged();
            }
        }

        #endregion

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
                if (_examContent != null)
                {
                    e.Cancel = true; // Can't close during exam
                    System.Windows.MessageBox.Show("You cannot close the exam window. Please submit your exam first.",
                        "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            this.Loaded += (s, e) =>
            {
                // Remove window chrome using WinAPI
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                int style = GetWindowLong(helper.Handle, GWL_STYLE);
                SetWindowLong(helper.Handle, GWL_STYLE, style & ~WS_SY_MENU);

                // Keep window on top
                SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

                // Prevent screenshot
                _screenshotPrevention.ProtectWindow(helper.Handle);
            };

            // Capture all keyboard input
            PreviewKeyDown += OnPreviewKeyDown;
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
                var result = await _sessionManager.StartSession(studentId, sessionToken, examId);
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

                // Populate the question list for the UI navigation panel
                Questions.Clear();
                for (int i = 0; i < _examContent.Questions.Count; i++)
                {
                    Questions.Add(new QuestionViewModel
                    {
                        Id = _examContent.Questions[i].Id,
                        Number = i + 1
                    });
                }

                // Display first question
                if (_examContent.Questions.Any())
                {
                    DisplayQuestion(_examContent.Questions[0]);
                }

                // Start timers
                _examTimer.Start();
                _autoSaveTimer.Start();

                // Update UI
                StudentId = studentId;
                ExamTitle = _examContent.Title;
                QuestionProgress = $"Question {_currentQuestionIndex + 1} of {_examContent.Questions.Count}";
                
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to start exam: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void DisplayQuestion(Question question)
        {
            CurrentQuestionNumber = _currentQuestionIndex + 1;
            _currentQuestion = question;

            // Update question display
            CurrentQuestionText = question.Text;

            // Update question navigation
            foreach (var q in Questions)
            {
                q.IsCurrent = q.Id == question.Id;
            }

            // Load saved answer if exists
            if (_studentAnswers.ContainsKey(question.Id))
            {
                LoadSavedAnswer(question.Id);
            }
            else
            {
                // Clear the answer box for a new question
                AnswerTextBox.Text = string.Empty;
            }

            // Update navigation buttons
            PreviousButton.IsEnabled = _currentQuestionIndex > 0;
            NextButton.IsEnabled = _currentQuestionIndex < _examContent.Questions.Count - 1;
        }

        private void SaveCurrentAnswer()
        {
            if (_currentQuestion == null) return;

            // Get answer based on question type
            string answer = GetCurrentAnswer();
            if (!string.IsNullOrEmpty(answer))
            {
                _studentAnswers[_currentQuestion.Id] = answer;
                
                // Update question status
                var questionVM = Questions.FirstOrDefault(q => q.Id == _currentQuestion.Id);
                if (questionVM != null)
                {
                    questionVM.IsAnswered = true;
                }
            }
        }

        private string GetCurrentAnswer()
        {
            return AnswerTextBox.Text;
        }

        private void LoadSavedAnswer(string questionId)
        {
            if (_studentAnswers.ContainsKey(questionId))
            {
                AnswerTextBox.Text = _studentAnswers[questionId];
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
            QuestionProgress = $"Question {_currentQuestionIndex + 1} of {_examContent.Questions.Count}";
        }

        #region Event Handlers

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToQuestion(_currentQuestionIndex - 1);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToQuestion(_currentQuestionIndex + 1);
        }

        private void QuestionNavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string questionId)
            {
                var questionIndex = _examContent.Questions.FindIndex(q => q.Id == questionId);
                if (questionIndex >= 0)
                {
                    NavigateToQuestion(questionIndex);
                }
            }
        }

        private async void SubmitButton_Click(object sender, RoutedEventArgs e)
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
                StudentId = this.StudentId,
                ExamId = _examContent.ExamId,
                Answers = _studentAnswers,
                SubmissionTime = DateTime.Now,
                TimeSpent = _examContent.Duration - _remainingTime
            };

            // Save submission
            await SaveSubmission(submission);

            // End session
            _sessionManager.EndSession(true);

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

        #endregion

        private async Task SaveSubmission(ExamSubmission submission)
        {
            await Task.Run(() =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(submission, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var encrypted = EncryptData(json);
                var filename = $"submission_{submission.StudentId}_{DateTime.Now:yyyyMMdd_HHmmss}.dat";
                string submissionPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Submissions",
                    filename);

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(submissionPath));
                System.IO.File.WriteAllBytes(submissionPath, encrypted);
            });
        }

        private byte[] EncryptData(string data)
        {
            // For now, simple encoding. In production, use proper AES encryption
            return System.Text.Encoding.UTF8.GetBytes(data);
        }

        private void UpdateExamTimer(object sender, EventArgs e)
        {
            _remainingTime = _remainingTime.Subtract(TimeSpan.FromSeconds(1));
            
            // Update display
            RemainingTimeDisplay = $"Time Remaining: {_remainingTime.Hours:00}:{_remainingTime.Minutes:00}:{_remainingTime.Seconds:00}";
            
            if (_remainingTime <= TimeSpan.Zero)
            {
                _examTimer.Stop();
                System.Windows.MessageBox.Show("Time is up! The exam will now be submitted automatically.", 
                    "Time's Up", MessageBoxButton.OK, MessageBoxImage.Information);
                SubmitButton_Click(this, new RoutedEventArgs());
                return;
            }
        }

        private void AutoSaveAnswers(object sender, EventArgs e)
        {
            SaveCurrentAnswer();
            LogSecurityEvent("Auto-save completed");
        }

        private void LogSecurityEvent(string message)
        {
            try
            {
                string logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Logs",
                    $"exam_log_{DateTime.Now:yyyyMMdd}.txt");

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Silently fail if logging doesn't work
            }
        }

        private void CleanupAndExit()
        {
            try
            {
                _examTimer?.Stop();
                _autoSaveTimer?.Stop();
                _processMonitor?.StopMonitoring();
                _screenshotPrevention?.StopProtection();
                _captureBlocker?.StopBlocking();

                _screenshotPrevention?.Dispose();
                _processMonitor?.Dispose();
            }
            catch (Exception ex)
            {
                LogSecurityEvent($"Cleanup error: {ex.Message}");
            }

            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            CleanupAndExit();
            base.OnClosed(e);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
