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
    public class LabCredentialManager
    {
        private readonly string _credentialsPath;
        private readonly string _sessionsPath;
        private readonly string _usedCredentialsPath;

        public LabCredentialManager()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SecureExam",
                "Credentials"
            );
            Directory.CreateDirectory(appData);

            _credentialsPath = Path.Combine(appData, "lab_credentials.json");
            _sessionsPath = Path.Combine(appData, "active_sessions.json");
            _usedCredentialsPath = Path.Combine(appData, "used_credentials.json");
        }

        public class ValidationResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string ExamId { get; set; }
            public string LabId { get; set; }
            public string StudentName { get; set; }
        }

        public class StudentCredential
        {
            public string StudentId { get; set; }
            public string StudentName { get; set; }
            public string LoginCode { get; set; }
            public string ExamId { get; set; }
            public string LabId { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
            public bool IsUsed { get; set; }
            public string DeviceId { get; set; }
            public DateTime? UsedAt { get; set; }
        }

        public class ActiveSession
        {
            public string StudentId { get; set; }
            public string DeviceId { get; set; }
            public string ComputerName { get; set; }
            public DateTime StartedAt { get; set; }
            public bool IsActive { get; set; }
        }

        public ValidationResult ValidateStudentLogin(string studentId, string code)
        {
            try
            {
                var credentials = LoadCredentials();
                var credential = credentials.FirstOrDefault(c =>
                    c.StudentId.Equals(studentId, StringComparison.OrdinalIgnoreCase) &&
                    c.LoginCode == code);

                if (credential == null)
                {
                    return new ValidationResult
                    {
                        Success = false,
                        Message = "Invalid Student ID or Login Code"
                    };
                }

                if (credential.IsUsed)
                {
                    return new ValidationResult
                    {
                        Success = false,
                        Message = "This login code has already been used. Request a new one from your invigilator."
                    };
                }

                if (DateTime.Now > credential.ExpiresAt)
                {
                    return new ValidationResult
                    {
                        Success = false,
                        Message = "This login code has expired. Request a new one from your invigilator."
                    };
                }

                return new ValidationResult
                {
                    Success = true,
                    Message = "Credentials validated successfully",
                    ExamId = credential.ExamId,
                    LabId = credential.LabId,
                    StudentName = credential.StudentName
                };
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    Success = false,
                    Message = $"Validation error: {ex.Message}"
                };
            }
        }

        public void MarkCredentialAsUsed(string studentId, string code)
        {
            try
            {
                var credentials = LoadCredentials();
                var credential = credentials.FirstOrDefault(c =>
                    c.StudentId.Equals(studentId, StringComparison.OrdinalIgnoreCase) &&
                    c.LoginCode == code);

                if (credential != null)
                {
                    credential.IsUsed = true;
                    credential.UsedAt = DateTime.Now;
                    credential.DeviceId = GetDeviceId();
                    SaveCredentials(credentials);

                    // Log to used credentials
                    LogUsedCredential(credential);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error marking credential as used: {ex.Message}");
            }
        }

        public System.Threading.Tasks.Task<bool> TerminatePreviousSession(string studentId)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var sessions = LoadActiveSessions();
                    var existingSession = sessions.FirstOrDefault(s =>
                        s.StudentId.Equals(studentId, StringComparison.OrdinalIgnoreCase) &&
                        s.IsActive);

                    if (existingSession != null)
                    {
                        existingSession.IsActive = false;
                        SaveActiveSessions(sessions);
                        LogEvent($"Terminated previous session for {studentId} on {existingSession.ComputerName}");
                        return true;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    LogError($"Error terminating session: {ex.Message}");
                    return false;
                }
            });
        }

        public void CreateActiveSession(string studentId)
        {
            try
            {
                var sessions = LoadActiveSessions();

                var newSession = new ActiveSession
                {
                    StudentId = studentId,
                    DeviceId = GetDeviceId(),
                    ComputerName = Environment.MachineName,
                    StartedAt = DateTime.Now,
                    IsActive = true
                };

                sessions.Add(newSession);
                SaveActiveSessions(sessions);
            }
            catch (Exception ex)
            {
                LogError($"Error creating session: {ex.Message}");
            }
        }

        private List<StudentCredential> LoadCredentials()
        {
            try
            {
                if (!File.Exists(_credentialsPath))
                    return new List<StudentCredential>();

                string json = File.ReadAllText(_credentialsPath);
                return JsonSerializer.Deserialize<List<StudentCredential>>(json) ?? new List<StudentCredential>();
            }
            catch
            {
                return new List<StudentCredential>();
            }
        }

        private void SaveCredentials(List<StudentCredential> credentials)
        {
            try
            {
                string json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_credentialsPath, json);
            }
            catch (Exception ex)
            {
                LogError($"Error saving credentials: {ex.Message}");
            }
        }

        private List<ActiveSession> LoadActiveSessions()
        {
            try
            {
                if (!File.Exists(_sessionsPath))
                    return new List<ActiveSession>();

                string json = File.ReadAllText(_sessionsPath);
                return JsonSerializer.Deserialize<List<ActiveSession>>(json) ?? new List<ActiveSession>();
            }
            catch
            {
                return new List<ActiveSession>();
            }
        }

        private void SaveActiveSessions(List<ActiveSession> sessions)
        {
            try
            {
                string json = JsonSerializer.Serialize(sessions, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_sessionsPath, json);
            }
            catch (Exception ex)
            {
                LogError($"Error saving sessions: {ex.Message}");
            }
        }

        private void LogUsedCredential(StudentCredential credential)
        {
            try
            {
                var usedCreds = new List<StudentCredential>();
                if (File.Exists(_usedCredentialsPath))
                {
                    string json = File.ReadAllText(_usedCredentialsPath);
                    usedCreds = JsonSerializer.Deserialize<List<StudentCredential>>(json) ?? new List<StudentCredential>();
                }

                usedCreds.Add(credential);

                string newJson = JsonSerializer.Serialize(usedCreds, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_usedCredentialsPath, newJson);
            }
            catch { }
        }

        private string GetDeviceId()
        {
            try
            {
                string hwId = Environment.MachineName;
                using (var sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(hwId));
                    return Convert.ToBase64String(hash).Substring(0, 12);
                }
            }
            catch
            {
                return "UNKNOWN";
            }
        }

        private void LogEvent(string message)
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Credentials", "Logs", $"credentials_{DateTime.Now:yyyyMMdd}.log"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        private void LogError(string message)
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam", "Credentials", "Logs", $"errors_{DateTime.Now:yyyyMMdd}.log"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}\n");
            }
            catch { }
        }
    }
}
