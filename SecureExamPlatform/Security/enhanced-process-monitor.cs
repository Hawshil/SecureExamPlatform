using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SecureExamPlatform.Security
{
    public class EnhancedProcessMonitor
    {
        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private bool _isMonitoring;

        private readonly string[] _blockedProcesses = new[]
        {
            // Browsers
            "chrome", "firefox", "msedge", "opera", "brave", "iexplore", "safari",
            
            // Communication apps
            "teams", "slack", "discord", "zoom", "skype", "telegram", "whatsapp",
            "signal", "viber", "messenger",
            
            // Screen sharing/recording
            "obs64", "obs32", "obs", "camtasia", "snagit", "ShareX", "OBS Studio",
            "anydesk", "teamviewer", "chrome-remote-desktop", "vnc", "ammyy",
            
            // AI assistants
            "cursor", "copilot", "codeium", "tabnine",
            
            // Note-taking
            "notepad++", "sublime_text", "code", "atom", "onenote"
        };

        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            _isMonitoring = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _monitoringTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        CheckAndTerminateBlockedProcesses();
                        await Task.Delay(2000, _cancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogError($"Process monitoring error: {ex.Message}");
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;
            _cancellationTokenSource?.Cancel();

            try
            {
                _monitoringTask?.Wait(5000);
            }
            catch { }
        }

        private void CheckAndTerminateBlockedProcesses()
        {
            try
            {
                var processes = Process.GetProcesses();

                foreach (var process in processes)
                {
                    try
                    {
                        string processName = process.ProcessName.ToLower();

                        if (_blockedProcesses.Any(blocked => processName.Contains(blocked)))
                        {
                            LogEvent($"Terminating blocked process: {process.ProcessName}");
                            process.Kill();
                            process.WaitForExit(2000);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Process may have already exited
                        if (!process.HasExited)
                        {
                            LogError($"Failed to terminate {process.ProcessName}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error checking processes: {ex.Message}");
            }
        }

        private void LogEvent(string message)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Security", "Logs", $"process_monitor_{DateTime.Now:yyyyMMdd}.log"
                );
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        private void LogError(string message)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Security", "Logs", $"process_errors_{DateTime.Now:yyyyMMdd}.log"
                );
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}\n");
            }
            catch { }
        }
    }
}
