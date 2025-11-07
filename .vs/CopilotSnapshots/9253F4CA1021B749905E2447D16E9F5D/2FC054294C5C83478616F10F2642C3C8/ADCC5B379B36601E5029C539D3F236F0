using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecureExamPlatform.Security;

namespace SecureExamPlatform.Core
{
    public class StudentCredential
    {
        public string StudentId { get; set; }
        public string ExamId { get; set; }
        public string HardwareId { get; set; }
        public string AccessToken { get; set; }
        public string TotpSecret { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; }
        public string ComputerName { get; set; }
        public int MaxAttempts { get; set; }
        public int AttemptsUsed { get; set; }
    }

    [JsonSerializable(typeof(List<StudentCredential>))]
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    )]
    public partial class CredentialJsonContext : JsonSerializerContext { }

    public class CredentialManager
    {
        private List<StudentCredential> credentials;
        private readonly TimeZoneInfo _istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        private string GetCredentialsPath()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SecureExam");
            Directory.CreateDirectory(appDataPath);
            return Path.Combine(appDataPath, "exam_credentials.json");
        }

        public CredentialManager()
        {
            LoadCredentials();
        }

        public StudentCredential GenerateCredential(string studentId, string examId,
            string hardwareId, string computerName, int validityMinutes = 180)
        {
            // Use UTC internally, add validity minutes
            DateTime nowUtc = DateTime.UtcNow;

            var credential = new StudentCredential
            {
                StudentId = studentId,
                ExamId = examId,
                HardwareId = hardwareId,
                ComputerName = computerName,
                AccessToken = GenerateAccessToken(),
                TotpSecret = TotpManager.GenerateSecret(),
                CreatedAt = nowUtc,
                ExpiresAt = nowUtc.AddMinutes(validityMinutes),
                IsUsed = false,
                MaxAttempts = 1,
                AttemptsUsed = 0
            };

            credentials.Add(credential);
            SaveCredentials();

            return credential;
        }

        public (bool success, string message, StudentCredential credential) ValidateCredential(
            string studentId, string accessToken, string hardwareId, string totpCode)
        {
            var credential = credentials.Find(c =>
                c.StudentId == studentId &&
                c.AccessToken == accessToken &&
                !c.IsUsed);

            if (credential == null)
            {
                return (false, "Invalid credentials or already used", null);
            }

            // Compare using UTC
            if (DateTime.UtcNow > credential.ExpiresAt)
            {
                return (false, "Credentials have expired", null);
            }

            if (credential.HardwareId != hardwareId)
            {
                return (false, "Hardware mismatch - credentials are bound to another system", null);
            }

            if (credential.AttemptsUsed >= credential.MaxAttempts)
            {
                return (false, "Maximum login attempts exceeded", null);
            }

            if (!TotpManager.ValidateCode(credential.TotpSecret, totpCode))
            {
                credential.AttemptsUsed++;
                SaveCredentials();
                return (false, $"Invalid authentication code. {credential.MaxAttempts - credential.AttemptsUsed} attempts remaining", null);
            }

            credential.IsUsed = true;
            credential.AttemptsUsed++;
            SaveCredentials();

            return (true, "Authentication successful", credential);
        }

        public void InvalidateAllCredentialsForStudent(string studentId, string examId)
        {
            foreach (var cred in credentials)
            {
                if (cred.StudentId == studentId && cred.ExamId == examId)
                {
                    cred.IsUsed = true;
                }
            }
            SaveCredentials();
        }

        public void UpdateCredential(StudentCredential credential)
        {
            var existing = credentials.FindIndex(c =>
                c.StudentId == credential.StudentId &&
                c.AccessToken == credential.AccessToken);

            if (existing >= 0)
            {
                credentials[existing] = credential;
                SaveCredentials();
            }
        }

        private string GenerateAccessToken()
        {
            byte[] tokenBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(tokenBytes);
            }
            return Convert.ToBase64String(tokenBytes)
                .Replace("+", "")
                .Replace("/", "")
                .Replace("=", "")
                .Substring(0, 16)
                .ToUpper();
        }
        private void LoadCredentials()
        {
            string path = GetCredentialsPath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    credentials = JsonSerializer.Deserialize(
                        json,
                        CredentialJsonContext.Default.ListStudentCredential)
                        ?? new List<StudentCredential>();
                }
                catch (Exception ex)
                {
                    // log and fallback
                    File.AppendAllText(Path.Combine(Path.GetDirectoryName(path) ?? ".", "load_error.log"),
                        $"[{DateTime.UtcNow:O}] Failed to load credentials: {ex}\n");
                    credentials = new List<StudentCredential>();
                }
            }
            else
            {
                credentials = new List<StudentCredential>();
            }
            // Diagnostic write
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(path) ?? ".", "load_debug.log"),
                $"[{DateTime.UtcNow:O}] Loaded {credentials.Count} credentials from {path}\n");
        }


        private void SaveCredentials()
        {
            string path = GetCredentialsPath();
            string json = JsonSerializer.Serialize(
                credentials,
                CredentialJsonContext.Default.ListStudentCredential);
            File.WriteAllText(path, json);
        }

        public List<StudentCredential> GetAllCredentials()
        {
            return new List<StudentCredential>(credentials);
        }
    }
}
