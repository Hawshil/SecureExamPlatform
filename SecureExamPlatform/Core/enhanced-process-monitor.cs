using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SecureExamPlatform.Core
{
    public class EnhancedProcessMonitor : IDisposable
    {
        private readonly System.Threading.Timer _monitorTimer;
        private readonly HashSet<string> _blockedProcesses;
        private readonly HashSet<string> _blockedWindowTitles;
        private readonly List<SecurityViolation> _violations;
        private bool _isMonitoring;

        // Import Windows APIs for window enumeration
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        private const int SW_MINIMIZE = 6;

        public EnhancedProcessMonitor()
        {
            _violations = new List<SecurityViolation>();
            
            // Comprehensive list of blocked processes
            _blockedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // System tools
                "taskmgr", "procexp", "procexp64", "ProcessHacker",
                "cmd", "powershell", "pwsh", "bash", "wsl",
                "regedit", "regedt32", "msconfig", "mmc",
                "devmgmt", "compmgmt", "services", "perfmon",
                
                // Browsers and communication
                "chrome", "firefox", "msedge", "iexplore", "opera", "brave",
                "discord", "slack", "teams", "zoom", "skype", "telegram",
                "whatsapp", "signal",
                
                // Developer tools
                "devenv", "code", "sublime_text", "notepad++", "atom",
                "idea", "idea64", "pycharm", "pycharm64", "webstorm",
                "eclipse", "netbeans",
                
                // Debugging and reverse engineering
                "ollydbg", "x64dbg", "x32dbg", "windbg", "ida", "ida64",
                "ghidra", "radare2", "immunity debugger", "cheatengine",
                "artmoney", "tsearch", "wireshark", "fiddler", "charles",
                
                // Virtual machines
                "vmware", "vmplayer", "virtualbox", "vboxmanage", "qemu",
                "parallels", "vmsvc", "vmusrvc", "vboxtray",
                
                // Screen capture
                "obs", "obs64", "streamlabs", "xsplit", "nvidia broadcast",
                "bandicam", "fraps", "camtasia", "snagit", "greenshot",
                "sharex", "lightshot", "gyazo", "flameshot",
                
                // Remote access
                "teamviewer", "anydesk", "realvnc", "tightvnc", "ultravnc",
                "mstsc", "rdpclip", "logmein", "gotomypc", "chrome remote",
                
                // File managers and utilities
                "explorer++", "totalcmd", "7zfm", "winrar",
                "everything", "listary", "wox", "keypirinha",
                
                // System modification
                "autohotkey", "autoit", "macro recorder", "jitbit",
                "processlasso", "sandboxie", "comodo"
            };
            
            // Window titles that indicate cheating attempts
            _blockedWindowTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "task manager", "process explorer", "process hacker",
                "command prompt", "powershell", "terminal",
                "registry editor", "services", "computer management",
                "google", "stack overflow", "github", "chat gpt", "claude",
                "whatsapp web", "telegram web", "discord",
                "remote desktop", "teamviewer", "anydesk",
                "virtual machine", "vmware", "virtualbox",
                "cheat", "hack", "crack", "bypass"
            };

            // Start monitoring
            _monitorTimer = new System.Threading.Timer(MonitorSystem, null, 1000, 1000);
            _isMonitoring = true;
        }

        private void MonitorSystem(object state)
        {
            if (!_isMonitoring) return;
            
            try
            {
                // Monitor processes
                MonitorProcesses();
                
                // Monitor windows
                MonitorWindows();
                
                // Check for suspicious activity patterns
                DetectSuspiciousPatterns();
            }
            catch (Exception ex)
            {
                LogError($"Monitoring error: {ex.Message}");
            }
        }

        private void MonitorProcesses()
        {
            var processes = Process.GetProcesses();
            
            foreach (var process in processes)
            {
                try
                {
                    // Check process name
                    if (_blockedProcesses.Contains(process.ProcessName))
                    {
                        KillProcess(process, "Blocked process");
                        continue;
                    }
                    
                    // Check process window title
                    if (!string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        foreach (var blockedTitle in _blockedWindowTitles)
                        {
                            if (process.MainWindowTitle.IndexOf(blockedTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                KillProcess(process, $"Blocked window title: {blockedTitle}");
                                break;
                            }
                        }
                    }
                    
                    // Check process path for suspicious locations
                    try
                    {
                        string path = process.MainModule?.FileName?.ToLower() ?? "";
                        if (path.Contains("\\temp\\") || 
                            path.Contains("\\tmp\\") ||
                            path.Contains("\\downloads\\") ||
                            path.Contains("portable"))
                        {
                            LogSecurityViolation($"Suspicious process path: {path}", 
                                ViolationType.SuspiciousPath);
                        }
                    }
                    catch { /* Access denied for system processes */ }
                }
                catch (Exception ex)
                {
                    // Process might have exited
                    if (!process.HasExited)
                    {
                        LogError($"Error checking process {process.ProcessName}: {ex.Message}");
                    }
                }
            }
        }

        private void MonitorWindows()
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    int length = GetWindowTextLength(hWnd);
                    if (length > 0)
                    {
                        var sb = new StringBuilder(length + 1);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string windowTitle = sb.ToString();
                        
                        // Check against blocked titles
                        foreach (var blockedTitle in _blockedWindowTitles)
                        {
                            if (windowTitle.IndexOf(blockedTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Get process ID
                                uint processId;
                                GetWindowThreadProcessId(hWnd, out processId);
                                
                                try
                                {
                                    var process = Process.GetProcessById((int)processId);
                                    KillProcess(process, $"Blocked window: {windowTitle}");
                                }
                                catch
                                {
                                    // Try to minimize the window at least
                                    ShowWindow(hWnd, SW_MINIMIZE);
                                    LogSecurityViolation($"Minimized blocked window: {windowTitle}", 
                                        ViolationType.BlockedWindow);
                                }
                                break;
                            }
                        }
                    }
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);
        }

        private void DetectSuspiciousPatterns()
        {
            // Detect rapid process creation (possible evasion attempt)
            var recentProcesses = Process.GetProcesses()
                .Where(p => 
                {
                    try 
                    { 
                        return DateTime.Now - p.StartTime < TimeSpan.FromSeconds(5);
                    }
                    catch { return false; }
                })
                .ToList();
                
            if (recentProcesses.Count > 5)
            {
                LogSecurityViolation($"Suspicious activity: {recentProcesses.Count} processes started rapidly", 
                    ViolationType.RapidProcessCreation);
            }
            
            // Check for cmd/powershell child processes (common bypass technique)
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.ProcessName.Equals("conhost", StringComparison.OrdinalIgnoreCase))
                    {
                        // Conhost often indicates a console application is running
                        LogSecurityViolation("Console host detected - possible command line usage", 
                            ViolationType.ConsoleDetected);
                        process.Kill();
                    }
                }
                catch { }
            }
        }

        private void KillProcess(Process process, string reason)
        {
            try
            {
                string processInfo = $"{process.ProcessName} (PID: {process.Id})";
                
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000); // Wait up to 1 second
                    
                    LogSecurityViolation($"Terminated {processInfo} - {reason}", 
                        ViolationType.ProcessTerminated);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to terminate {process.ProcessName}: {ex.Message}");
                
                // Try alternative termination
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/F /PID {process.Id}",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    });
                }
                catch { }
            }
        }

        private void LogSecurityViolation(string message, ViolationType type)
        {
            var violation = new SecurityViolation
            {
                Timestamp = DateTime.Now,
                Message = message,
                Type = type,
                StudentId = Environment.UserName // Will be replaced with actual student ID
            };
            
            _violations.Add(violation);
            
            // Log to file
            string logPath = $"security_log_{DateTime.Now:yyyyMMdd}.txt";
            string logEntry = $"[{violation.Timestamp:yyyy-MM-dd HH:mm:ss}] [{type}] {message}";
            
            try
            {
                System.IO.File.AppendAllText(logPath, logEntry + Environment.NewLine);
            }
            catch { }
            
            Console.WriteLine($"SECURITY: {message}");
        }

        private void LogError(string message)
        {
            Console.WriteLine($"ERROR: {message}");
        }

        public List<SecurityViolation> GetViolations()
        {
            return new List<SecurityViolation>(_violations);
        }

        public void StartMonitoring()
        {
            _isMonitoring = true;
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
        }

        public void Dispose()
        {
            _isMonitoring = false;
            _monitorTimer?.Dispose();
        }
    }

    public class SecurityViolation
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public ViolationType Type { get; set; }
        public string StudentId { get; set; }
    }

    public enum ViolationType
    {
        ProcessTerminated,
        BlockedWindow,
        SuspiciousPath,
        RapidProcessCreation,
        ConsoleDetected,
        RegistryModification,
        NetworkActivity,
        FileSystemAccess,
        DebuggingDetected,
        VirtualMachineDetected
    }
}