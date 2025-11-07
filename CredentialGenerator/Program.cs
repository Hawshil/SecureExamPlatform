using System;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using SecureExamPlatform.Core;

namespace CredentialGenerator
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=============================");
            Console.WriteLine(" SecureExamPlatform Credential Generator");
            Console.WriteLine("=============================\n");

            Console.Write("Enter Student ID (e.g. STU001): ");
            string studentId = Console.ReadLine()?.Trim() ?? "STU001";

            Console.Write("Enter Exam ID (e.g. EXAM001): ");
            string examId = Console.ReadLine()?.Trim() ?? "EXAM001";

            Console.Write("Enter validity (minutes, default 180): ");
            string validityInput = (Console.ReadLine() ?? "").Trim();
            int validity = 180;
            if (int.TryParse(validityInput, out int mins)) validity = mins;

            string hardwareId = GenerateHardwareId();
            string computerName = Environment.MachineName;

            var credManager = new CredentialManager();
            var cred = credManager.GenerateCredential(
                studentId: studentId,
                examId: examId,
                hardwareId: hardwareId,
                computerName: computerName,
                validityMinutes: validity
            );

            // Get IST timezone
            TimeZoneInfo istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

            // Convert UTC times to IST for display
            DateTime createdIst = TimeZoneInfo.ConvertTimeFromUtc(cred.CreatedAt, istZone);
            DateTime expiresIst = TimeZoneInfo.ConvertTimeFromUtc(cred.ExpiresAt, istZone);
            DateTime nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);

            Console.WriteLine("\n========================================");
            Console.WriteLine("CREDENTIALS GENERATED:");
            Console.WriteLine("========================================");
            Console.WriteLine($"Student ID:      {cred.StudentId}");
            Console.WriteLine($"Exam ID:         {cred.ExamId}");
            Console.WriteLine($"Access Token:    {cred.AccessToken}");
            Console.WriteLine($"TOTP Secret:     {cred.TotpSecret}");
            Console.WriteLine($"Hardware ID:     {hardwareId}");
            Console.WriteLine($"Computer Name:   {computerName}");
            Console.WriteLine($"");
            Console.WriteLine($"Created (IST):   {createdIst:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Valid Until (IST): {expiresIst:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Current Time (IST): {nowIst:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"");
            Console.WriteLine($"Validity: {validity} minutes");
            Console.WriteLine("========================================\n");

            Console.WriteLine("✓ Credentials saved to:");
            Console.WriteLine($"  {GetCredentialsPath()}\n");

            Console.WriteLine("NEXT STEPS:");
            Console.WriteLine("1. Open your Authenticator app (Google/Microsoft)");
            Console.WriteLine("2. Add account → Manual entry");
            Console.WriteLine($"   Account Name: {studentId}");
            Console.WriteLine($"   Secret Key: {cred.TotpSecret}");
            Console.WriteLine("   Time-based: YES");
            Console.WriteLine("");
            Console.WriteLine("3. Start the SecureExamPlatform app");
            Console.WriteLine($"4. Enter Student ID: {studentId}");
            Console.WriteLine($"5. Enter Access Token: {cred.AccessToken}");
            Console.WriteLine("6. Enter the 6-digit code from authenticator");
            Console.WriteLine("");
            Console.WriteLine("Press Enter to exit...");
            Console.ReadLine();
        }

        private static string GetCredentialsPath()
        {
            try
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam");

                Directory.CreateDirectory(appDataPath);
                return Path.Combine(appDataPath, "exam_credentials.json");
            }
            catch
            {
                return "exam_credentials.json (local)";
            }
        }

        private static string GenerateHardwareId()
        {
            try
            {
                StringBuilder fingerprint = new StringBuilder();

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystemProduct"))
                {
                    foreach (var obj in searcher.Get())
                        fingerprint.Append(obj["UUID"]?.ToString() ?? "");
                }

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard"))
                {
                    foreach (var obj in searcher.Get())
                        fingerprint.Append(obj["SerialNumber"]?.ToString() ?? "");
                }

                using (var sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(fingerprint.ToString()));
                    return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
                }
            }
            catch
            {
                return "UNKNOWN-HARDWARE";
            }
        }
    }
}
