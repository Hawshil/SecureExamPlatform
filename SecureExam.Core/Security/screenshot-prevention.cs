using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SecureExam.Core.Security
{
    public class ScreenshotPrevention : IDisposable
    {
        private readonly System.Threading.Timer _clipboardTimer;
        private readonly Thread _overlayThread;
        private bool _isProtecting;
        private Form _protectionOverlay;
        
        // Windows API imports
        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
        
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardViewer(IntPtr hWndNewViewer);
        
        [DllImport("user32.dll")]
        private static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);
        
        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();
        
        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        
        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();
        
        // DWM (Desktop Window Manager) API for screen capture blocking
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        private const uint WDA_NONE = 0x00000000;
        private const int DWMWA_EXCLUDED_FROM_PEEK = 12;
        
        // Hotkey IDs
        private const int HOTKEY_PRINTSCREEN = 1;
        private const int HOTKEY_ALT_PRINTSCREEN = 2;
        private const int HOTKEY_WIN_SHIFT_S = 3;
        private const int HOTKEY_WIN_PRINTSCREEN = 4;
        
        // Virtual key codes
        private const int VK_SNAPSHOT = 0x2C; // Print Screen
        private const int VK_S = 0x53;
        private const int MOD_ALT = 0x0001;
        private const int MOD_WIN = 0x0008;
        private const int MOD_SHIFT = 0x0004;

        public ScreenshotPrevention()
        {
            _isProtecting = true;

            // Start clipboard monitoring
            _clipboardTimer = new System.Threading.Timer(ClearClipboard, null, 100, 100);

            // Start protection overlay thread for advanced protection
            _overlayThread = new Thread(RunProtectionOverlay)
            {
                IsBackground = true,
                Name = "ScreenProtection"
            };
            _overlayThread.SetApartmentState(ApartmentState.STA);
        }

        public void ProtectWindow(IntPtr windowHandle)
        {
            try
            {
                // Set window display affinity to exclude from capture
                // This prevents the window from appearing in screenshots and screen recordings
                SetWindowDisplayAffinity(windowHandle, WDA_EXCLUDEFROMCAPTURE);
                
                // Also set DWM attribute to exclude from peek
                int excludeFromPeek = 1;
                DwmSetWindowAttribute(windowHandle, DWMWA_EXCLUDED_FROM_PEEK, 
                    ref excludeFromPeek, sizeof(int));
                
                // Register hotkeys to block screenshot shortcuts
                RegisterHotKey(windowHandle, HOTKEY_PRINTSCREEN, 0, VK_SNAPSHOT);
                RegisterHotKey(windowHandle, HOTKEY_ALT_PRINTSCREEN, MOD_ALT, VK_SNAPSHOT);
                RegisterHotKey(windowHandle, HOTKEY_WIN_SHIFT_S, MOD_WIN | MOD_SHIFT, VK_S);
                RegisterHotKey(windowHandle, HOTKEY_WIN_PRINTSCREEN, MOD_WIN, VK_SNAPSHOT);
                
                Console.WriteLine("Screenshot protection enabled for window");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error protecting window: {ex.Message}");
            }
        }

        public void UnprotectWindow(IntPtr windowHandle)
        {
            try
            {
                // Remove display affinity
                SetWindowDisplayAffinity(windowHandle, WDA_NONE);
                
                // Unregister hotkeys
                UnregisterHotKey(windowHandle, HOTKEY_PRINTSCREEN);
                UnregisterHotKey(windowHandle, HOTKEY_ALT_PRINTSCREEN);
                UnregisterHotKey(windowHandle, HOTKEY_WIN_SHIFT_S);
                UnregisterHotKey(windowHandle, HOTKEY_WIN_PRINTSCREEN);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error unprotecting window: {ex.Message}");
            }
        }

        private void ClearClipboard(object state)
        {
            if (!_isProtecting) return;
            
            try
            {
                // Check if clipboard contains image data
                if (Clipboard.ContainsImage())
                {
                    // Clear the clipboard
                    if (OpenClipboard(IntPtr.Zero))
                    {
                        EmptyClipboard();
                        CloseClipboard();
                        
                        LogSecurityEvent("Clipboard image cleared - possible screenshot attempt");
                    }
                }
                
                // Also clear if clipboard contains specific text patterns
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (ContainsSuspiciousContent(text))
                    {
                        Clipboard.Clear();
                        LogSecurityEvent($"Suspicious clipboard content cleared");
                    }
                }
            }
            catch
            {
                // Clipboard might be in use by another process
            }
        }

        private bool ContainsSuspiciousContent(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            
            string[] suspiciousPatterns = {
                "answer", "solution", "question", 
                "stackoverflow", "github", "chatgpt",
                "google", "chegg", "coursehero"
            };
            
            string lowerText = text.ToLower();
            foreach (var pattern in suspiciousPatterns)
            {
                if (lowerText.Contains(pattern))
                    return true;
            }
            
            return false;
        }

        private void RunProtectionOverlay()
        {
            try
            {
                // Create a transparent overlay that prevents screen capture tools
                _protectionOverlay = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    WindowState = FormWindowState.Maximized,
                    TopMost = false, // Don't interfere with exam window
                    ShowInTaskbar = false,
                    TransparencyKey = Color.Magenta,
                    BackColor = Color.Magenta,
                    Opacity = 0.01 // Nearly invisible
                };
                
                // Set the overlay to be excluded from capture
                _protectionOverlay.Load += (s, e) =>
                {
                    ProtectWindow(_protectionOverlay.Handle);
                };
                
                // This form will block certain screen capture tools
                Application.Run(_protectionOverlay);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Overlay protection error: {ex.Message}");
            }
        }

        public void StartProtection()
        {
            _isProtecting = true;
            
            if (!_overlayThread.IsAlive)
            {
                _overlayThread.Start();
            }
        }

        public void StopProtection()
        {
            _isProtecting = false;
            
            try
            {
                _protectionOverlay?.Invoke(new Action(() => _protectionOverlay.Close()));
            }
            catch { }
        }

        private void LogSecurityEvent(string message)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SCREENSHOT_PREVENTION: {message}";
            Console.WriteLine(logEntry);
            
            try
            {
                System.IO.File.AppendAllText($"security_log_{DateTime.Now:yyyyMMdd}.txt", 
                    logEntry + Environment.NewLine);
            }
            catch { }
        }

        public void Dispose()
        {
            StopProtection();
            _clipboardTimer?.Dispose();
        }
    }

    /// <summary>
    /// Additional protection using Windows hooks
    /// </summary>
    public class ScreenCaptureBlocker
    {
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;
        
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, 
            IntPtr hMod, uint dwThreadId);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_SNAPSHOT = 0x2C;
        
        public void StartBlocking()
        {
            _proc = HookCallback;
            _hookId = SetHook(_proc);
        }
        
        public void StopBlocking()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
        
        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }
        
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                
                // Block Print Screen key
                if (vkCode == VK_SNAPSHOT)
                {
                    Console.WriteLine("Print Screen blocked");
                    return (IntPtr)1; // Block the key
                }
            }
            
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}