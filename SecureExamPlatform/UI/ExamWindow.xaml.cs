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
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SecureExamPlatform.UI
{
    public partial class ExamWindow : Window, INotifyPropertyChanged
    {
        // Question organization
        private class ExamSection
        {
            public string Name { get; set; }
            public List<Question> Questions { get; set; } = new List<Question>();
        }

        private readonly ExamSessionManager _sessionManager;
        private EnhancedProcessMonitor _processMonitor;
        private ScreenshotPrevention _screenshotPrevention;
        private ScreenCaptureBlocker _captureBlocker;

        private ExamContent _examContent;
        private Dictionary<string, string> _studentAnswers;
        private List<ExamSection> _sections;
        private int _currentSectionIndex;
        private int _currentQuestionIndex;
        private Question _currentQuestion;

        private DispatcherTimer _examTimer;
        private DispatcherTimer _autoSaveTimer;
        private TimeSpan _remainingTime;
        private bool isSubmitted = false;

        private RadioButton[] _mcqOptions;
        private readonly HashSet<string> _attemptedQuestions = new HashSet<string>();
        private readonly HashSet<string> _flaggedQuestions = new HashSet<string>();

        // Section colors for visual distinction
        private readonly string[] _sectionColors = { "#2e7d32", "#1565c0", "#c62828", "#f57c00", "#6a1b9a" };

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;

        public ExamWindow()
        {
            try
            {
                InitializeComponent();
                DataContext = this;

                _sessionManager = new ExamSessionManager();
                _studentAnswers = new Dictionary<string, string>();
                _sections = new List<ExamSection>();

                _examTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _examTimer.Tick += UpdateExamTimer;

                _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _autoSaveTimer.Tick += AutoSaveAnswers;

                this.Closing += Window_Closing;
                this.Loaded += Window_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize exam window: {ex.Message}", "Initialization Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Make fullscreen
            this.WindowState = WindowState.Maximized;
            this.WindowStyle = WindowStyle.None;
            this.Topmost = true;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_examContent != null && !isSubmitted)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to exit? This will end your exam session.\n\nYour answers have been auto-saved but the exam will be marked as incomplete.",
                    "Confirm Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                try
                {
                    SaveCurrentAnswer();
                    _examTimer?.Stop();
                    _autoSaveTimer?.Stop();
                    _sessionManager?.EndSession(false);
                    CleanupAndExit();
                }
                catch { }
            }
        }

        public async Task<bool> StartExam(string studentId, string sessionToken, string examId)
        {
            try
            {
                var result = await _sessionManager.StartSession(studentId, sessionToken, examId);

                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage, "Login Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                _examContent = result.ExamContent;
                _remainingTime = _examContent.Duration;

                // Organize questions into sections (auto-detect or use first 10% as section A, next 10% as B, etc.)
                OrganizeIntoSections();

                StudentId = studentId;
                UpdateQuestionProgress();
                OnPropertyChanged(nameof(ExamTitle));

                // Display first section and question
                if (_sections.Any() && _sections[0].Questions.Any())
                {
                    _currentSectionIndex = 0;
                    _currentQuestionIndex = 0;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        BuildNavigationUI();
                        DisplayQuestion(_sections[0].Questions[0]);
                    });
                }

                // Start timers
                _examTimer.Start();
                _autoSaveTimer.Start();

                // Initialize security AFTER UI is ready
                await Task.Delay(500);
                await Dispatcher.InvokeAsync(() =>
                {
                    ConfigureKioskMode();
                    StartSecurityMonitoring();
                });

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start exam: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void OrganizeIntoSections()
        {
            // Auto-detect sections based on question types or create equal sections
            // For now, create sections of ~30 questions each
            const int questionsPerSection = 30;
            int sectionCount = (int)Math.Ceiling(_examContent.Questions.Count / (double)questionsPerSection);

            for (int i = 0; i < sectionCount; i++)
            {
                var section = new ExamSection
                {
                    Name = $"Section {(char)('A' + i)}",
                    Questions = _examContent.Questions
                        .Skip(i * questionsPerSection)
                        .Take(questionsPerSection)
                        .ToList()
                };
                _sections.Add(section);
            }
        }

        private void BuildNavigationUI()
        {
            var navPanel = FindName("NavigationPanel") as StackPanel;
            if (navPanel == null) return;

            navPanel.Children.Clear();

            // Section tabs
            var sectionTabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

            for (int i = 0; i < _sections.Count; i++)
            {
                int sectionIndex = i; // Capture for closure
                var btn = new Button
                {
                    Content = _sections[i].Name,
                    Width = 120,
                    Height = 40,
                    Margin = new Thickness(5, 0, 5, 0),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_sectionColors[i % _sectionColors.Length])),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Tag = sectionIndex
                };
                btn.Click += (s, e) => SwitchSection(sectionIndex);
                sectionTabs.Children.Add(btn);
            }
            navPanel.Children.Add(sectionTabs);

            // Question grid for current section
            var questionGrid = new UniformGrid
            {
                Columns = 5,
                Margin = new Thickness(0, 10, 0, 0)
            };

            UpdateQuestionGrid(questionGrid);
            navPanel.Children.Add(new ScrollViewer
            {
                Content = questionGrid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 400
            });
        }

        private void UpdateQuestionGrid(UniformGrid grid)
        {
            grid.Children.Clear();

            if (_currentSectionIndex >= _sections.Count) return;

            var currentSection = _sections[_currentSectionIndex];

            for (int i = 0; i < currentSection.Questions.Count; i++)
            {
                var question = currentSection.Questions[i];
                int questionIndex = i; // Capture for closure

                var btn = new Button
                {
                    Content = $"Q{i + 1}",
                    Height = 40,
                    Margin = new Thickness(2),
                    Tag = question.Id
                };

                // Set color based on status
                if (_attemptedQuestions.Contains(question.Id))
                    btn.Background = Brushes.Green;
                else if (_flaggedQuestions.Contains(question.Id))
                    btn.Background = Brushes.Orange;
                else
                    btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444"));

                btn.Foreground = Brushes.White;
                btn.Click += (s, e) => NavigateToQuestion(questionIndex);

                grid.Children.Add(btn);
            }
        }

        private void SwitchSection(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= _sections.Count) return;

            SaveCurrentAnswer();
            _currentSectionIndex = sectionIndex;
            _currentQuestionIndex = 0;

            var navPanel = FindName("NavigationPanel") as StackPanel;
            if (navPanel?.Children.Count > 1 && navPanel.Children[1] is ScrollViewer sv && sv.Content is UniformGrid grid)
            {
                UpdateQuestionGrid(grid);
            }

            DisplayQuestion(_sections[sectionIndex].Questions[0]);
            UpdateQuestionProgress();
        }

        private void DisplayQuestion(Question question)
        {
            try
            {
                _currentQuestion = question;

                // Calculate global question number
                int globalQuestionNum = 1;
                for (int i = 0; i < _currentSectionIndex; i++)
                {
                    globalQuestionNum += _sections[i].Questions.Count;
                }
                globalQuestionNum += _currentQuestionIndex + 1;

                CurrentQuestionNumber = globalQuestionNum;
                OnPropertyChanged(nameof(CurrentQuestionText));
                OnPropertyChanged(nameof(CurrentSectionName));

                var answerArea = FindName("AnswerArea") as Grid;
                if (answerArea == null) return;

                // Clear previous content completely
                answerArea.Children.Clear();
                answerArea.RowDefinitions.Clear();

                // Add label row
                answerArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                // Add answer row
                answerArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var label = new TextBlock
                {
                    Text = "Your Answer:",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#aaa")),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid.SetRow(label, 0);
                answerArea.Children.Add(label);

                if (question.Type == QuestionType.MultipleChoice && question.Options != null && question.Options.Count > 0)
                {
                    // MCQ - show radio buttons
                    var optionsPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                    _mcqOptions = new RadioButton[question.Options.Count];

                    for (int i = 0; i < question.Options.Count; i++)
                    {
                        var radio = new RadioButton
                        {
                            Content = question.Options[i],
                            GroupName = $"MCQOptions_{question.Id}", // Unique group per question
                            Margin = new Thickness(0, 8, 0, 8),
                            FontSize = 14,
                            Foreground = Brushes.White,
                            Tag = i
                        };
                        radio.Checked += McqOption_Checked;
                        _mcqOptions[i] = radio;
                        optionsPanel.Children.Add(radio);
                    }

                    Grid.SetRow(optionsPanel, 1);
                    answerArea.Children.Add(optionsPanel);

                    // Restore saved answer
                    if (_studentAnswers.ContainsKey(question.Id))
                    {
                        string savedAnswer = _studentAnswers[question.Id];
                        if (int.TryParse(savedAnswer, out int selectedIndex) &&
                            selectedIndex >= 0 && selectedIndex < _mcqOptions.Length)
                        {
                            _mcqOptions[selectedIndex].IsChecked = true;
                        }
                    }
                }
                else
                {
                    // Text answer - show textbox
                    var textBox = new TextBox
                    {
                        Name = "AnswerTextBox",
                        MinHeight = 200,
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0a0a0a")),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444")),
                        Padding = new Thickness(10),
                        FontSize = 14,
                        Text = _studentAnswers.ContainsKey(question.Id) ? _studentAnswers[question.Id] : string.Empty
                    };
                    textBox.TextChanged += AnswerTextBox_TextChanged;

                    Grid.SetRow(textBox, 1);
                    answerArea.Children.Add(textBox);

                    // Register name for later access
                    RegisterName("AnswerTextBox", textBox);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying question: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void McqOption_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && _currentQuestion != null)
            {
                int selectedIndex = (int)radio.Tag;
                _studentAnswers[_currentQuestion.Id] = selectedIndex.ToString();
                _attemptedQuestions.Add(_currentQuestion.Id);
                UpdateNavigationUI();
                ShowAutoSaveIndicator();
            }
        }

        private void SaveCurrentAnswer()
        {
            if (_currentQuestion == null) return;
            string answer = GetCurrentAnswer();
            if (!string.IsNullOrEmpty(answer))
            {
                _studentAnswers[_currentQuestion.Id] = answer;
            }
        }

        private string GetCurrentAnswer()
        {
            if (_currentQuestion == null) return string.Empty;

            if (_currentQuestion.Type == QuestionType.MultipleChoice && _mcqOptions != null)
            {
                for (int i = 0; i < _mcqOptions.Length; i++)
                {
                    if (_mcqOptions[i].IsChecked == true)
                    {
                        return i.ToString();
                    }
                }
                return string.Empty;
            }
            else
            {
                try
                {
                    var answerBox = FindName("AnswerTextBox") as TextBox;
                    return answerBox?.Text ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private void NavigateToQuestion(int index)
        {
            if (_currentSectionIndex >= _sections.Count) return;
            if (index < 0 || index >= _sections[_currentSectionIndex].Questions.Count) return;

            SaveCurrentAnswer();
            _currentQuestionIndex = index;
            DisplayQuestion(_sections[_currentSectionIndex].Questions[index]);
            UpdateQuestionProgress();
        }

        private void OnPreviousQuestion()
        {
            if (_currentQuestionIndex > 0)
            {
                NavigateToQuestion(_currentQuestionIndex - 1);
            }
            else if (_currentSectionIndex > 0)
            {
                // Go to previous section's last question
                SwitchSection(_currentSectionIndex - 1);
                _currentQuestionIndex = _sections[_currentSectionIndex].Questions.Count - 1;
                NavigateToQuestion(_currentQuestionIndex);
            }
        }

        private void OnNextQuestion()
        {
            if (_currentQuestionIndex < _sections[_currentSectionIndex].Questions.Count - 1)
            {
                NavigateToQuestion(_currentQuestionIndex + 1);
            }
            else if (_currentSectionIndex < _sections.Count - 1)
            {
                // Go to next section's first question
                SwitchSection(_currentSectionIndex + 1);
            }
        }

        private async void OnSubmitExam()
        {
            var result = MessageBox.Show(
                $"Are you sure you want to submit your exam?\n\nQuestions Answered: {_studentAnswers.Count} / {_examContent.Questions.Count}\n\nThis action cannot be undone.",
                "Confirm Submission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            SaveCurrentAnswer();
            _examTimer.Stop();
            _autoSaveTimer.Stop();

            var submission = new ExamSubmission
            {
                SessionId = _sessionManager.GetCurrentSession()?.SessionId ?? Guid.NewGuid().ToString(),
                StudentId = this.StudentId,
                ExamId = _examContent.ExamId,
                Answers = _studentAnswers,
                SubmissionTime = DateTime.Now,
                TimeSpent = _examContent.Duration - _remainingTime
            };

            await SaveSubmission(submission);
            isSubmitted = true;
            _sessionManager.EndSession(true);

            MessageBox.Show(
                $"✓ Exam Submitted Successfully!\n\nStudent ID: {submission.StudentId}\nSubmission Time: {submission.SubmissionTime:yyyy-MM-dd HH:mm:ss}\nTime Spent: {submission.TimeSpent.TotalMinutes:F0} minutes\nQuestions Answered: {submission.Answers.Count}/{_examContent.Questions.Count}",
                "Exam Submitted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            CleanupAndExit();
            Application.Current.Shutdown();
        }

        private async Task SaveSubmission(ExamSubmission submission)
        {
            await Task.Run(() =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(submission, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var filename = $"submission_{submission.StudentId}_{submission.ExamId}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                string submissionPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Submissions", filename);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(submissionPath));
                System.IO.File.WriteAllText(submissionPath, json);
            });
        }

        private void UpdateExamTimer(object sender, EventArgs e)
        {
            _remainingTime = _remainingTime.Subtract(TimeSpan.FromSeconds(1));
            if (_remainingTime <= TimeSpan.Zero)
            {
                _examTimer.Stop();
                MessageBox.Show("⏰ Time's Up!\n\nThe exam will now be submitted automatically.", "Time's Up", MessageBoxButton.OK, MessageBoxImage.Information);
                OnSubmitExam();
                return;
            }
            OnPropertyChanged(nameof(RemainingTimeDisplay));
            if (_remainingTime.TotalMinutes == 5 && _remainingTime.Seconds == 0)
            {
                MessageBox.Show("⚠ You have 5 minutes remaining!", "Time Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AutoSaveAnswers(object sender, EventArgs e)
        {
            SaveCurrentAnswer();
        }

        private void ConfigureKioskMode()
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                if (helper.Handle == IntPtr.Zero) return;

                int style = GetWindowLong(helper.Handle, GWL_STYLE);
                SetWindowLong(helper.Handle, GWL_STYLE, style & ~WS_SYSMENU);
                SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

                PreviewKeyDown += OnPreviewKeyDown;

                if (_screenshotPrevention != null)
                {
                    _screenshotPrevention.ProtectWindow(helper.Handle);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Kiosk mode warning: {ex.Message}");
            }
        }

        private void StartSecurityMonitoring()
        {
            try
            {
                _processMonitor = new EnhancedProcessMonitor();
                _screenshotPrevention = new ScreenshotPrevention();
                _captureBlocker = new ScreenCaptureBlocker();

                _processMonitor.StartMonitoring();
                _captureBlocker.StartBlocking();
                _screenshotPrevention.StartProtection();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Security warning: {ex.Message}");
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Block Alt+Tab, Alt+F4, Windows key
            if (Keyboard.Modifiers == ModifierKeys.Alt && (e.Key == Key.Tab || e.Key == Key.F4 || e.Key == Key.Escape))
            {
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Windows || e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
                return;
            }

            // Allow Ctrl+Shift+Esc for emergency exit
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Escape)
            {
                var result = MessageBox.Show(
                    "Emergency Exit?\n\nThis will terminate the exam. Your progress will be saved but marked incomplete.\n\nAre you absolutely sure?",
                    "Emergency Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    SaveCurrentAnswer();
                    _sessionManager?.EndSession(false);
                    CleanupAndExit();
                    Application.Current.Shutdown();
                }
                e.Handled = true;
            }
        }

        private void CleanupAndExit()
        {
            try
            {
                _processMonitor?.StopMonitoring();
                _screenshotPrevention?.StopProtection();
                _captureBlocker?.StopBlocking();
                _processMonitor?.Dispose();
                _screenshotPrevention?.Dispose();
            }
            catch { }
        }

        private void UpdateQuestionProgress()
        {
            int totalQuestions = _sections.Sum(s => s.Questions.Count);
            int currentGlobal = _sections.Take(_currentSectionIndex).Sum(s => s.Questions.Count) + _currentQuestionIndex + 1;
            QuestionProgress = $"Question {currentGlobal} of {totalQuestions}";
            OnPropertyChanged(nameof(QuestionProgress));
        }

        private void UpdateNavigationUI()
        {
            var navPanel = FindName("NavigationPanel") as StackPanel;
            if (navPanel?.Children.Count > 1 && navPanel.Children[1] is ScrollViewer sv && sv.Content is UniformGrid grid)
            {
                UpdateQuestionGrid(grid);
            }
        }

        private void ShowAutoSaveIndicator()
        {
            Dispatcher.Invoke(() =>
            {
                var indicator = FindName("AutoSaveIndicator") as TextBlock;
                if (indicator != null)
                {
                    indicator.Visibility = Visibility.Visible;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, e) =>
                    {
                        indicator.Visibility = Visibility.Collapsed;
                        timer.Stop();
                    };
                    timer.Start();
                }
            });
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e) => OnPreviousQuestion();
        private void NextButton_Click(object sender, RoutedEventArgs e) => OnNextQuestion();
        private void SubmitButton_Click(object sender, RoutedEventArgs e) => OnSubmitExam();

        private void FlagButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentQuestion != null)
            {
                if (_flaggedQuestions.Contains(_currentQuestion.Id))
                {
                    _flaggedQuestions.Remove(_currentQuestion.Id);
                }
                else
                {
                    _flaggedQuestions.Add(_currentQuestion.Id);
                }
                UpdateNavigationUI();
            }
        }

        private void AnswerTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentQuestion != null && sender is TextBox textBox)
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    _attemptedQuestions.Add(_currentQuestion.Id);
                }
                ShowAutoSaveIndicator();
            }
        }

        public string ExamTitle => _examContent?.Title ?? "Loading Exam...";

        private string _studentId = "N/A";
        public string StudentId
        {
            get => _studentId;
            set { _studentId = value; OnPropertyChanged(nameof(StudentId)); }
        }

        private string _questionProgress = "Loading...";
        public string QuestionProgress
        {
            get => _questionProgress;
            set { _questionProgress = value; OnPropertyChanged(nameof(QuestionProgress)); }
        }

        public string CurrentQuestionText => _currentQuestion?.Text ?? "";
        public string CurrentSectionName => _currentSectionIndex < _sections.Count ? _sections[_currentSectionIndex].Name : "";
        public string RemainingTimeDisplay => $"{_remainingTime.Hours:D2}:{_remainingTime.Minutes:D2}:{_remainingTime.Seconds:D2}";

        private int _currentQuestionNumber = 1;
        public int CurrentQuestionNumber
        {
            get => _currentQuestionNumber;
            set
            {
                _currentQuestionNumber = value;
                OnPropertyChanged(nameof(CurrentQuestionNumber));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}