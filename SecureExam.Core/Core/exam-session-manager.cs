using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureExam.Core.Core;
using SecureExam.Core.Models;
using SecureExam.Core.Security;

namespace SecureExam.Core.Core
{
    public class ExamSessionManager
    {
        private readonly string _sessionsDirectory;
        private ExamSession? _currentSession;

        public class ExamSession
        {
            public string SessionId { get; set; } = string.Empty;
            public string StudentId { get; set; } = string.Empty;
            public string ExamId { get; set; } = string.Empty;
            public string DeviceId { get; set; } = string.Empty;
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public bool IsActive { get; set; }
            public Dictionary<int, string> SavedAnswers { get; set; } = new Dictionary<int, string>();
        }

        public ExamSessionManager()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SecureExam",
                "Sessions"
            );
            Directory.CreateDirectory(appData);
            _sessionsDirectory = appData;
        }

        public string CreateSession(string studentId, string examId, string deviceId)
        {
            var session = new ExamSession
            {
                SessionId = GenerateSessionId(),
                StudentId = studentId,
                ExamId = examId,
                DeviceId = deviceId,
                StartTime = DateTime.Now,
                IsActive = true,
                SavedAnswers = new Dictionary<int, string>()
            };

            _currentSession = session;
            SaveSession(session);
            return session.SessionId;
        }

        public void SaveAnswers(Dictionary<int, string> answers)
        {
            if (_currentSession == null) return;

            _currentSession.SavedAnswers = answers;
            SaveSession(_currentSession);
        }

        public Dictionary<int, string>? LoadSavedAnswers(string studentId, string examId)
        {
            var session = LoadSession(studentId, examId);
            return session?.SavedAnswers;
        }

        public void EndSession()
        {
            if (_currentSession == null) return;

            _currentSession.IsActive = false;
            _currentSession.EndTime = DateTime.Now;
            SaveSession(_currentSession);
            _currentSession = null;
        }

        public void TerminateOtherSessions(string studentId, string examId, string currentDeviceId)
        {
            try
            {
                var sessionFiles = Directory.GetFiles(_sessionsDirectory, $"{studentId}_{examId}_*.json");

                foreach (var file in sessionFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var session = JsonSerializer.Deserialize<ExamSession>(json);

                        if (session != null && session.DeviceId != currentDeviceId && session.IsActive)
                        {
                            session.IsActive = false;
                            session.EndTime = DateTime.Now;

                            string updatedJson = JsonSerializer.Serialize(session, new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });
                            File.WriteAllText(file, updatedJson);
                        }
                    }
                    catch
                    {
                        // Skip invalid files
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error terminating sessions: {ex.Message}");
            }
        }

        private void SaveSession(ExamSession session)
        {
            try
            {
                string filename = $"{session.StudentId}_{session.ExamId}_{session.DeviceId}.json";
                string filepath = Path.Combine(_sessionsDirectory, filename);

                string json = JsonSerializer.Serialize(session, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(filepath, json);
            }
            catch (Exception ex)
            {
                LogError($"Error saving session: {ex.Message}");
            }
        }

        private ExamSession? LoadSession(string studentId, string examId)
        {
            try
            {
                var sessionFiles = Directory.GetFiles(_sessionsDirectory, $"{studentId}_{examId}_*.json");

                foreach (var file in sessionFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var session = JsonSerializer.Deserialize<ExamSession>(json);

                        if (session != null && session.IsActive)
                        {
                            return session;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error loading session: {ex.Message}");
            }

            return null;
        }

        private string GenerateSessionId()
        {
            byte[] randomBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes).Replace("/", "_").Replace("+", "-").Substring(0, 22);
        }

        private void LogError(string message)
        {
            try
            {
                string logPath = Path.Combine(_sessionsDirectory, "session_errors.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }
    }
}
