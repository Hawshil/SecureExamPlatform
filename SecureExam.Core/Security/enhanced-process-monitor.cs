using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SecureExam.Security
{
    public class EnhancedProcessMonitor
    {
        private CancellationTokenSource cancellationToken;
        private Task monitoringTask;

        private readonly string[] bannedProcesses = new[]
        {
            // Browsers
            "chrome", "firefox", "msedge", "opera", "brave", "safari",
            "iexplore", "vivaldi", "yandex", "seamonkey",
            
            // Communication apps
            "whatsapp", "telegram", "discord", "slack", "teams", "skype",
            "zoom", "messenger", "signal", "viber",
            
            // Screen capture
            "snagit", "sharex", "greenshot", "lightshot", "obs", "obs64",
            "camtasia", "bandicam", "fraps", "nvidia", "geforce",
            
            // Remote desktop
            "teamviewer", "anydesk", "chrome remote desktop", "vnc",
            
            // Developer tools
            "fiddler", "wireshark", "postman",
            
            // Note apps
            "onenote", "evernote", "notion",
            
            // AI assistants
            "copilot", "chatgpt"
        };

        public void StartMonitoring()
        {
            if (monitoringTask != null) return;

            cancellationToken = new CancellationTokenSource();
            monitoringTask = Task.Run(() => MonitorProcesses(cancellationToken.Token));
        }

        public void StopMonitoring()
        {
            cancellationToken?.Cancel();
            monitoringTask?.Wait(1000);
        }

        private void MonitorProcesses(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var processes = Process.GetProcesses();

                    foreach (var process in processes)
                    {
                        try
                        {
                            string processName = process.ProcessName.ToLower();

                            if (bannedProcesses.Any(banned => processName.Contains(banned)))
                            {
                                try
                                {
                                    process.Kill();
                                    LogSecurityEvent($"Killed banned process: {process.ProcessName}");
                                }
                                catch { }
                            }
                        }
                        catch { }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch { }

                Thread.Sleep(1000);
            }
        }

        private void LogSecurityEvent(string message)
        {
            try
            {
                string logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Security",
                    "Logs",
                    $"security_{DateTime.Now:yyyyMMdd}.log"
                );

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }
    }
}
