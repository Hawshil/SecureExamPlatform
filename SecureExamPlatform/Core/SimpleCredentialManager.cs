using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;

namespace SecureExamPlatform.Core
{
    public class SimpleCredential
    {
        public string StudentId { get; set; }
        public string ExamId { get; set; }
        public string LoginCode { get; set; }
        public int RemainingLogins { get; set; }
        public bool IsUsed { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SimpleCredentialManager
    {
        private List<SimpleCredential> _credentials;
        private readonly string _credPath;

        public SimpleCredentialManager()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SecureExam");
            Directory.CreateDirectory(appData);
            _credPath = Path.Combine(appData, "simple_credentials.json");
            Load();
        }

        public SimpleCredential GenerateCredential(string studentId, string examId, int allowedLogins = 1)
        {
            var cred = new SimpleCredential
            {
                StudentId = studentId,
                ExamId = examId,
                LoginCode = GenerateCode(),
                RemainingLogins = allowedLogins,
                IsUsed = false,
                CreatedAt = DateTime.Now
            };

            _credentials.Add(cred);
            Save();
            return cred;
        }

        public (bool success, string message, string examId) ValidateLogin(string studentId, string code)
        {
            var cred = _credentials.FirstOrDefault(c =>
                c.StudentId == studentId &&
                c.LoginCode == code &&
                !c.IsUsed);

            if (cred == null)
                return (false, "Invalid Student ID or Login Code", null);

            if (cred.RemainingLogins <= 0)
                return (false, "No remaining login attempts", null);

            // Use one login
            cred.RemainingLogins--;
            if (cred.RemainingLogins == 0)
                cred.IsUsed = true;

            Save();
            return (true, "Success", cred.ExamId);
        }

        private string GenerateCode()
        {
            var random = new Random();
            return $"{random.Next(1000, 9999)}{random.Next(1000, 9999)}";
        }

        private void Load()
        {
            if (File.Exists(_credPath))
            {
                try
                {
                    string json = File.ReadAllText(_credPath);
                    _credentials = JsonSerializer.Deserialize<List<SimpleCredential>>(json)
                        ?? new List<SimpleCredential>();
                }
                catch
                {
                    _credentials = new List<SimpleCredential>();
                }
            }
            else
            {
                _credentials = new List<SimpleCredential>();
            }
        }

        private void Save()
        {
            string json = JsonSerializer.Serialize(_credentials, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_credPath, json);
        }

        public List<SimpleCredential> GetAllCredentials() => new List<SimpleCredential>(_credentials);
    }
}
