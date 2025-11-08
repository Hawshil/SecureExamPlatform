using System;
using Xunit;
using SecureExamPlatform.Core;
using SecureExamPlatform.Security;

namespace SecureExamPlatform.Tests.Core
{
    public class CredentialManagerTests
    {
        private readonly CredentialManager _credentialManager;

        public CredentialManagerTests()
        {
            _credentialManager = new CredentialManager();
        }

        [Fact]
        public void GenerateCredential_ShouldCreateValidCredential()
        {
            // Arrange
            string studentId = "STU001";
            string examId = "EXAM001";
            string hardwareId = "HW123";
            string computerName = "TEST-PC";
            int validityMinutes = 180;

            // Act
            var credential = _credentialManager.GenerateCredential(
                studentId, examId, hardwareId, computerName, validityMinutes);

            // Assert
            Assert.NotNull(credential);
            Assert.Equal(studentId, credential.StudentId);
            Assert.Equal(examId, credential.ExamId);
            Assert.Equal(hardwareId, credential.HardwareId);
            Assert.Equal(computerName, credential.ComputerName);
            Assert.False(credential.IsUsed);
            Assert.NotNull(credential.AccessToken);
            Assert.NotNull(credential.TotpSecret);
            Assert.Equal(validityMinutes, (credential.ExpiresAt - credential.CreatedAt).TotalMinutes);
        }

        [Fact]
        public void ValidateCredential_WithValidCredentials_ShouldSucceed()
        {
            // Arrange
            string studentId = "STU001";
            string examId = "EXAM001";
            string hardwareId = "HW123";
            string computerName = "TEST-PC";

            var credential = _credentialManager.GenerateCredential(
                studentId, examId, hardwareId, computerName);

            string totpCode = TotpManager.GenerateCode(credential.TotpSecret);

            // Act
            var (success, message, validatedCred) = _credentialManager.ValidateCredential(
                studentId, credential.AccessToken, hardwareId, totpCode);

            // Assert
            Assert.True(success);
            Assert.Equal("Authentication successful", message);
            Assert.NotNull(validatedCred);
            Assert.True(validatedCred.IsUsed); // Credential should be marked as used after successful validation
        }

        [Fact]
        public void ValidateCredential_WithInvalidHardwareId_ShouldFail()
        {
            // Arrange
            string studentId = "STU001";
            string examId = "EXAM001";
            string hardwareId = "HW123";
            string computerName = "TEST-PC";

            var credential = _credentialManager.GenerateCredential(
                studentId, examId, hardwareId, computerName);

            string totpCode = TotpManager.GenerateCode(credential.TotpSecret);
            string wrongHardwareId = "WRONG-HW";

            // Act
            var (success, message, validatedCred) = _credentialManager.ValidateCredential(
                studentId, credential.AccessToken, wrongHardwareId, totpCode);

            // Assert
            Assert.False(success);
            Assert.Equal("Hardware mismatch - credentials are bound to another system", message);
            Assert.Null(validatedCred);
        }

        [Fact]
        public void ValidateCredential_WithInvalidTotpCode_ShouldFail()
        {
            // Arrange
            string studentId = "STU001";
            string examId = "EXAM001";
            string hardwareId = "HW123";
            string computerName = "TEST-PC";

            var credential = _credentialManager.GenerateCredential(
                studentId, examId, hardwareId, computerName);

            string wrongTotpCode = "000000";

            // Act
            var (success, message, validatedCred) = _credentialManager.ValidateCredential(
                studentId, credential.AccessToken, hardwareId, wrongTotpCode);

            // Assert
            Assert.False(success);
            Assert.Contains("Invalid authentication code", message);
            Assert.Null(validatedCred);
        }

        [Fact]
        public void ValidateCredential_WithExpiredCredential_ShouldFail()
        {
            // Arrange
            string studentId = "STU001";
            string examId = "EXAM001";
            string hardwareId = "HW123";
            string computerName = "TEST-PC";
            int validityMinutes = 0; // Expire immediately

            var credential = _credentialManager.GenerateCredential(
                studentId, examId, hardwareId, computerName, validityMinutes);

            string totpCode = TotpManager.GenerateCode(credential.TotpSecret);

            // Act
            var (success, message, validatedCred) = _credentialManager.ValidateCredential(
                studentId, credential.AccessToken, hardwareId, totpCode);

            // Assert
            Assert.False(success);
            Assert.Equal("Credentials have expired", message);
            Assert.Null(validatedCred);
        }

        [Fact]
        public void InvalidateAllCredentialsForStudent_ShouldMarkAllCredentialsAsUsed()
        {
            // Arrange
            string studentId = "STU001";
            string examId = "EXAM001";
            string hardwareId = "HW123";
            string computerName = "TEST-PC";

            // Generate multiple credentials
            _credentialManager.GenerateCredential(studentId, examId, hardwareId, computerName);
            _credentialManager.GenerateCredential(studentId, examId, hardwareId, computerName);

            // Act
            _credentialManager.InvalidateAllCredentialsForStudent(studentId, examId);

            // Assert
            var allCredentials = _credentialManager.GetAllCredentials();
            Assert.All(allCredentials, cred => 
                Assert.True(cred.StudentId != studentId || cred.ExamId != examId || cred.IsUsed));
        }
    }
}