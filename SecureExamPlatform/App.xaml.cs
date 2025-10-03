using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SecureExamPlatform
{
    /// <summary>
    /// Main application class with single instance enforcement
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private Mutex _appMutex;
        private const string APP_MUTEX_NAME = "SecureExamPlatform_SingleInstance";

        // Import for bringing window to foreground
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Check for single instance
            bool createdNew;
            _appMutex = new Mutex(true, APP_MUTEX_NAME, out createdNew);

            if (!createdNew)
            {
                // Another instance is already running
                System.Windows.MessageBox.Show(
                    "Secure Exam Platform is already running.\n\nOnly one instance can run at a time.",
                    "Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Try to bring existing instance to front
                BringExistingInstanceToFront();

                System.Windows.Application.Current.Shutdown();
                return;
            }

            // Set up global exception handling
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Check system requirements
            if (!CheckSystemRequirements())
            {
                System.Windows.MessageBox.Show(
                    "Your system does not meet the minimum requirements:\n\n" +
                    "• Windows 10 or later\n" +
                    "• 2GB RAM minimum\n" +
                    "• .NET Framework 4.7.2 or later",
                    "System Requirements",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                System.Windows.Application.Current.Shutdown();
                return;
            }

            // Create necessary directories
            CreateApplicationDirectories();

            // Log application start
            LogApplicationEvent("Application started");

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Release mutex
            _appMutex?.ReleaseMutex();
            _appMutex?.Dispose();

            // Log application exit
            LogApplicationEvent("Application exited");

            base.OnExit(e);
        }

        private void BringExistingInstanceToFront()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(currentProcess.ProcessName);

                foreach (var process in processes)
                {
                    if (process.Id != currentProcess.Id)
                    {
                        IntPtr handle = process.MainWindowHandle;
                        if (handle != IntPtr.Zero)
                        {
                            ShowWindow(handle, SW_RESTORE);
                            SetForegroundWindow(handle);
                        }
                        break;
                    }
                }
            }
            catch
            {
                // Silently fail if we can't bring to front
            }
        }

        private bool CheckSystemRequirements()
        {
            try
            {
                // Check Windows version
                var os = Environment.OSVersion;
                if (os.Platform != PlatformID.Win32NT || os.Version.Major < 10)
                {
                    return false;
                }

                // Check available RAM
                var memoryStatus = new MEMORYSTATUSEX();
                memoryStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memoryStatus))
                {
                    ulong totalMemoryMB = memoryStatus.ullTotalPhys / (1024 * 1024);
                    if (totalMemoryMB < 2048) // Less than 2GB
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                // If we can't check, assume requirements are met
                return true;
            }
        }

        private void CreateApplicationDirectories()
        {
            try
            {
                string appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam"
                );

                // Create main app directory
                Directory.CreateDirectory(appData);

                // Create subdirectories
                Directory.CreateDirectory(Path.Combine(appData, "Logs"));
                Directory.CreateDirectory(Path.Combine(appData, "Sessions"));
                Directory.CreateDirectory(Path.Combine(appData, "Submissions"));
                Directory.CreateDirectory(Path.Combine(appData, "Temp"));
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

                if (!e.IsTerminating)
                {
                    System.Windows.MessageBox.Show(
                        "An unexpected error occurred. The application may be unstable.\n\n" +
                        "Please save your work and restart the application.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "A critical error occurred and the application must close.\n\n" +
                        "Your exam progress has been saved.",
                        "Critical Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch
            {
                // Last resort - can't even show error dialog
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                LogApplicationEvent($"DISPATCHER EXCEPTION: {e.Exception}");

                // Handle specific known exceptions
                if (e.Exception is UnauthorizedAccessException)
                {
                    System.Windows.MessageBox.Show(
                        "Access denied. Please ensure the application has proper permissions.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    e.Handled = true;
                }
                else if (e.Exception is OutOfMemoryException)
                {
                    System.Windows.MessageBox.Show(
                        "The application is running low on memory. Please close other applications.",
                        "Memory Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    e.Handled = true;
                }
                else
                {
                    // Unknown exception - let default handler deal with it
                    e.Handled = false;
                }
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
                    "Logs",
                    $"app_{DateTime.Now:yyyyMMdd}.log"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(logPath));

                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(logPath, logEntry + Environment.NewLine);
            }
            catch
            {
                // Silently fail if logging doesn't work
            }
        }

        // Memory status structure for system requirements check
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