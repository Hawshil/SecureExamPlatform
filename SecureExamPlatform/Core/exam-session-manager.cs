using SecureExamPlatform.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Management;

namespace SecureExamPlatform.Core
{
    // --- Helper classes for the Session Manager ---
    public class SessionStartResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public ExamContent ExamContent { get; set; }
        public bool IsResumed { get; set; }
    }

    public class ExamSession
    {
        public string SessionId { get; set; }
        public string StudentId { get; set; }
        public string ExamId { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsActive { get; set; }
    }

    public class SessionEvent
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; }
        public string Message { get; set; }
    }


    public class ExamSessionManager
    {
        private readonly string _hardwareFingerprint;
        private ExamSession _currentSession;
        private readonly System.Threading.Timer _heartbeatTimer;
        private readonly List<SessionEvent> _sessionEvents;
        private readonly object _sessionLock = new object();

        private readonly string _sessionFile;
        private readonly string _sessionDirectory;

        public ExamSessionManager()
        {
            _hardwareFingerprint = GenerateHardwareFingerprint();
            _sessionEvents = new List<SessionEvent>();
            _heartbeatTimer = new System.Threading.Timer(SendHeartbeat, null, Timeout.Infinite, Timeout.Infinite);

            // Setup session directory and file paths
            _sessionDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SecureExam"
            );
            _sessionFile = Path.Combine(_sessionDirectory, "session.lock");

            // Ensure directory exists
            Directory.CreateDirectory(_sessionDirectory);
            
            // Clean up any existing session file on startup
            try
            {
                if (File.Exists(_sessionFile))
                {
                    File.Delete(_sessionFile);
                }
            }
            catch { }
        }

        // This method provides access to the current session details.
        public ExamSession GetCurrentSession()
        {
            return _currentSession;
        }

        // This method checks if a session is currently active.
        public bool IsSessionActive()
        {
            return _currentSession != null && _currentSession.IsActive;
        }

        /// <summary>
        /// Generates a unique hardware fingerprint for the current machine.
        /// </summary>
        private string GenerateHardwareFingerprint()
        {
            var components = new List<string>();

            // CPU ID
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    components.Add(obj["ProcessorId"]?.ToString() ?? "");
                    break;
                }
            }
            catch { components.Add("NO_CPU"); }

            // Motherboard Serial
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in searcher.Get())
                {
                    components.Add(obj["SerialNumber"]?.ToString() ?? "");
                    break;
                }
            }
            catch { components.Add("NO_MB"); }

            // Combine and hash
            string combined = string.Join("|", components);
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));

            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Starts a new exam session with the provided credentials.
        /// </summary>
        public async Task<SessionStartResult> StartSession(string studentId, string accessToken, string examId)
        {
            lock (_sessionLock)
            {
                if (_currentSession != null && _currentSession.IsActive)
                {
                    // Check if it's the same student trying to resume
                    if (_currentSession.StudentId == studentId && 
                        _currentSession.ExamId == examId)
                    {
                        return new SessionStartResult
                        {
                            Success = true,
                            ExamContent = GetMockExamContent(examId),
                            IsResumed = true
                        };
                    }
                    return new SessionStartResult 
                    { 
                        Success = false, 
                        ErrorMessage = "Another exam session is already active." 
                    };
                }

                // Start a new session
                _currentSession = new ExamSession
                {
                    SessionId = Guid.NewGuid().ToString(),
                    StudentId = studentId,
                    ExamId = examId,
                    StartTime = DateTime.UtcNow,
                    IsActive = true
                };

                try
                {
                    // Create session file with JSON content for better recovery
                    var sessionInfo = new
                    {
                        StudentId = studentId,
                        ExamId = examId,
                        StartTime = DateTime.UtcNow,
                        AccessToken = accessToken
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(sessionInfo);
                    File.WriteAllText(_sessionFile, json);

                    // Start heartbeat
                    _heartbeatTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

                    return new SessionStartResult
                    {
                        Success = true,
                        ExamContent = GetMockExamContent(examId),
                        IsResumed = false
                    };
                }
                catch (Exception ex)
                {
                    _currentSession = null;
                    return new SessionStartResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to start session: {ex.Message}"
                    };
                }
            }
        }

        /// <summary>
        /// Ends the current exam session and cleans up resources.
        /// </summary>
        public void EndSession(bool submitted)
        {
            lock (_sessionLock)
            {
                if (_currentSession == null) return;

                try
                {
                    _currentSession.IsActive = false;
                    _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);

                    // Clean up session file
                    if (File.Exists(_sessionFile))
                    {
                        File.Delete(_sessionFile);
                    }

                    _sessionEvents.Add(new SessionEvent
                    {
                        Timestamp = DateTime.Now,
                        EventType = "SessionEnd",
                        Message = $"Session ended. Submitted: {submitted}"
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error ending session: {ex.Message}");
                }
                finally
                {
                    _currentSession = null;
                }
            }
        }

        /// <summary>
        /// Periodically sends a signal to the server to confirm the session is still active.
        /// </summary>
        private void SendHeartbeat(object state)
        {
            if (_currentSession == null || !_currentSession.IsActive) return;

            // --- Placeholder for Server Communication ---
            Console.WriteLine($"[HEARTBEAT] Session {_currentSession.SessionId} is active.");

            _sessionEvents.Add(new SessionEvent { Timestamp = DateTime.Now, EventType = "Heartbeat", Message = "Session heartbeat sent." });
        }

        /// <summary>
        /// Helper method to generate sample exam data for testing.
        /// </summary>
        private ExamContent GetMockExamContent(string examId)
        {
            // In a real application, this data would come from a server or an encrypted file.
            return new ExamContent
            {
                ExamId = examId,
                Title = "CS101: Introduction to Programming Final",
                Duration = TimeSpan.FromMinutes(90),
                Questions = new List<Question>
                {
                    new Question { Id = "Q1", Text = "What is the main difference between a struct and a class in C#?" },
                    new Question { Id = "Q2", Text = "Explain the concept of polymorphism and provide a simple example." },
                    new Question { Id = "Q3", Text = "What is the purpose of the 'async' and 'await' keywords in C#?" }
                }
            };
        }
    }
}

//using SecureExamPlatform.Models;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Security.Cryptography;
//using System.Text;
//using System.Text.Json;
//using System.Threading;
//using System.Threading.Tasks;
//using System.Management;

//namespace SecureExamPlatform.Core
//{
//    // --- Helper classes for the Session Manager ---
//    public class SessionStartResult
//    {
//        public bool Success { get; set; }
//        public string ErrorMessage { get; set; }
//        public ExamContent ExamContent { get; set; }
//    }

//    public class ExamSession
//    {
//        public string SessionId { get; set; }
//        public string StudentId { get; set; }
//        public string ExamId { get; set; }
//        public DateTime StartTime { get; set; }
//        public bool IsActive { get; set; }
//    }

//    public class SessionEvent
//    {
//        public DateTime Timestamp { get; set; }
//        public string EventType { get; set; }
//        public string Message { get; set; }
//    }


//    public class ExamSessionManager
//    {
//        private readonly string _hardwareFingerprint;
//        private ExamSession _currentSession;
//        private readonly Timer _heartbeatTimer;
//        private readonly List<SessionEvent> _sessionEvents;
//        private readonly object _sessionLock = new object();

//        // Session state file - prevents multiple instances
//        private readonly string _sessionFile = Path.Combine(
//            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
//            "SecureExam",
//            "session.lock"
//        );

//        public ExamSessionManager()
//        {
//            _hardwareFingerprint = GenerateHardwareFingerprint();
//            _sessionEvents = new List<SessionEvent>();
//            // Heartbeat will be sent every 60 seconds
//            _heartbeatTimer = new Timer(SendHeartbeat, null, Timeout.Infinite, Timeout.Infinite);

//            // Ensure directory exists
//            Directory.CreateDirectory(Path.GetDirectoryName(_sessionFile));
//        }

//        // This method provides access to the current session details.
//        public ExamSession GetCurrentSession()
//        {
//            return _currentSession;
//        }

//        // This method checks if a session is currently active.
//        public bool IsSessionActive()
//        {
//            return _currentSession != null && _currentSession.IsActive;
//        }

//        /// <summary>
//        /// Generates a unique hardware fingerprint for the current machine.
//        /// </summary>
//        private string GenerateHardwareFingerprint()
//        {
//            var components = new List<string>();

//            // CPU ID
//            try
//            {
//                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
//                foreach (ManagementObject obj in searcher.Get())
//                {
//                    components.Add(obj["ProcessorId"]?.ToString() ?? "");
//                    break;
//                }
//            }
//            catch { components.Add("NO_CPU"); }

//            // Motherboard Serial
//            try
//            {
//                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
//                foreach (ManagementObject obj in searcher.Get())
//                {
//                    components.Add(obj["SerialNumber"]?.ToString() ?? "");
//                    break;
//                }
//            }
//            catch { components.Add("NO_MB"); }

//            // Combine and hash
//            string combined = string.Join("|", components);
//            using var sha256 = SHA256.Create();
//            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));

//            return Convert.ToBase64String(hash);
//        }

//        /// <summary>
//        /// Starts a new exam session with the provided credentials.
//        /// </summary>
//        public async Task<SessionStartResult> StartSession(string studentId, string accessToken, string examId)
//        {
//            lock (_sessionLock)
//            {
//                if (_currentSession != null && _currentSession.IsActive)
//                {
//                    // Check if it's the same student trying to resume
//                    if (_currentSession.StudentId == studentId && 
//                        _currentSession.ExamId == examId)
//                    {
//                        return new SessionStartResult
//                        {
//                            Success = true,
//                            ExamContent = GetMockExamContent(examId),
//                            IsResumed = true
//                        };
//                    }
//                    return new SessionStartResult 
//                    { 
//                        Success = false, 
//                        ErrorMessage = "Another exam session is already active." 
//                    };
//                }

//                // Start a new session
//                _currentSession = new ExamSession
//                {
//                    SessionId = Guid.NewGuid().ToString(),
//                    StudentId = studentId,
//                    ExamId = examId,
//                    StartTime = DateTime.UtcNow,
//                    IsActive = true
//                };

//                try
//                {
//                    // Create session file with JSON content for better recovery
//                    var sessionInfo = new
//                    {
//                        StudentId = studentId,
//                        ExamId = examId,
//                        StartTime = DateTime.UtcNow,
//                        AccessToken = accessToken
//                    };

//                    string json = System.Text.Json.JsonSerializer.Serialize(sessionInfo);
//                    File.WriteAllText(_sessionFile, json);

//                    // Start heartbeat
//                    _heartbeatTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

//                    return new SessionStartResult
//                    {
//                        Success = true,
//                        ExamContent = GetMockExamContent(examId),
//                        IsResumed = false
//                    };
//                }
//                catch (Exception ex)
//                {
//                    _currentSession = null;
//                    return new SessionStartResult
//                    {
//                        Success = false,
//                        ErrorMessage = $"Failed to start session: {ex.Message}"
//                    };
//                }
//            }
//        }

//        /// <summary>
//        /// Ends the current exam session and cleans up resources.
//        /// </summary>
//        public void EndSession(bool submitted)
//        {
//            lock (_sessionLock)
//            {
//                if (_currentSession == null) return;

//                try
//                {
//                    _currentSession.IsActive = false;
//                    _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);

//                    // Clean up session file
//                    if (File.Exists(_sessionFile))
//                    {
//                        File.Delete(_sessionFile);
//                    }

//                    _sessionEvents.Add(new SessionEvent
//                    {
//                        Timestamp = DateTime.Now,
//                        EventType = "SessionEnd",
//                        Message = $"Session ended. Submitted: {submitted}"
//                    });
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"Error ending session: {ex.Message}");
//                }
//                finally
//                {
//                    _currentSession = null;
//                }
//            }
//        }

//        /// <summary>
//        /// Periodically sends a signal to the server to confirm the session is still active.
//        /// </summary>
//        private void SendHeartbeat(object state)
//        {
//            if (_currentSession == null || !_currentSession.IsActive) return;

//            // --- Placeholder for Server Communication ---
//            // In a real app, you'd make an API call to your server here
//            // with _currentSession.SessionId to keep it from expiring.
//            Console.WriteLine($"[HEARTBEAT] Session {_currentSession.SessionId} is active.");

//            _sessionEvents.Add(new SessionEvent { Timestamp = DateTime.Now, EventType = "Heartbeat", Message = "Session heartbeat sent." });
//        }

//        /// <summary>
//        /// Helper method to generate sample exam data for testing.
//        /// </summary>
//        private ExamContent GetMockExamContent(string examId)
//        {
//            // In a real application, this data would come from a server or an encrypted file.
//            return new ExamContent
//            {
//                ExamId = examId,
//                Title = "CS101: Introduction to Programming Final",
//                Duration = TimeSpan.FromMinutes(90),
//                Questions = new List<Question>
//                {
//                    new Question { Id = "Q1", Text = "What is the main difference between a struct and a class in C#?" },
//                    new Question { Id = "Q2", Text = "Explain the concept of polymorphism and provide a simple example." },
//                    new Question { Id = "Q3", Text = "What is the purpose of the 'async' and 'await' keywords in C#?" }
//                }
//            };
//        }
//    }
//}