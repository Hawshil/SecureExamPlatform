using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SecureExamPlatform.UI
{
    public partial class App : Application
    {
        private Mutex _appMutex;
        private bool _ownsMutex;
        private const string APP_MUTEX_NAME = "SecureExamPlatform_Student_SingleInstance";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _appMutex = new Mutex(initiallyOwned: true, name: APP_MUTEX_NAME, createdNew: out _ownsMutex);

            if (!_ownsMutex)
            {
                MessageBox.Show(
                    "Student Exam Platform is already running.\n\nOnly one instance can run at a time.",
                    "Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }

            // Register global exception handlers
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            if (!CheckSystemRequirements())
            {
                MessageBox.Show(
                    "Your system does not meet the minimum requirements:\n\n" +
                    "• Windows 10 or later\n" +
                    "• 2GB RAM minimum\n" +
                    "• .NET 8.0 or later",
                    "System Requirements",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Current.Shutdown();
                return;
            }

            CreateApplicationDirectories();
            LogApplicationEvent("Student application started");

            base.OnStartup(e);

            // DIRECT TO STUDENT LOGIN (NO LAUNCHER)
            var studentLogin = new StudentLoginWindow();
            studentLogin.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_ownsMutex && _appMutex != null)
                {
                    _appMutex.ReleaseMutex();
                }
                _appMutex?.Dispose();
                LogApplicationEvent("Student application exited");
            }
            catch (Exception ex)
            {
                LogApplicationEvent($"Error during exit: {ex.Message}");
            }
            base.OnExit(e);
        }

        private bool CheckSystemRequirements()
        {
            try
            {
                var os = Environment.OSVersion;
                if (os.Platform != PlatformID.Win32NT || os.Version.Major < 10)
                {
                    return false;
                }

                var memoryStatus = new MEMORYSTATUSEX();
                memoryStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memoryStatus))
                {
                    ulong totalMemoryMB = memoryStatus.ullTotalPhys / (1024 * 1024);
                    if (totalMemoryMB < 2048)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        private void CreateApplicationDirectories()
        {
            try
            {
                string appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Student"
                );
                Directory.CreateDirectory(appData);
                Directory.CreateDirectory(Path.Combine(appData, "Logs"));
                Directory.CreateDirectory(Path.Combine(appData, "Sessions"));
                Directory.CreateDirectory(Path.Combine(appData, "Submissions"));
            }
            catch (Exception ex)
            {
                LogApplicationEvent($"Failed to create directories: {ex.Message}");
            }
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = e.ExceptionObject as Exception;
                string message = ex?.ToString() ?? "Unknown error occurred";
                LogApplicationEvent($"UNHANDLED EXCEPTION: {message}");

                if (e.IsTerminating)
                {
                    MessageBox.Show(
                        "A critical error occurred and the application must close.\n\n" +
                        "Your exam progress has been saved.",
                        "Critical Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch { }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                LogApplicationEvent($"DISPATCHER EXCEPTION: {e.Exception}");

                // Handle binding errors gracefully
                if (e.Exception.Message.Contains("TwoWay") || e.Exception.Message.Contains("OneWayToSource"))
                {
                    LogApplicationEvent("Binding error suppressed");
                    e.Handled = true;
                    return;
                }

                e.Handled = false;
            }
            catch
            {
                e.Handled = false;
            }
        }

        private void LogApplicationEvent(string message)
        {
            try
            {
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Student",
                    "Logs",
                    $"app_{DateTime.Now:yyyyMMdd}.log"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(logPath, logEntry + Environment.NewLine);
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
