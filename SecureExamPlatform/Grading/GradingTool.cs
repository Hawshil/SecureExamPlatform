using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SecureExamPlatform.Models;

namespace SecureExamPlatform.Grading
{
    public class GradingTool
    {
        private string submissionsDirectory;
        private string examsDirectory;

        public class GradeResult
        {
            public string StudentId { get; set; }
            public string ExamId { get; set; }
            public int TotalQuestions { get; set; }
            public int CorrectAnswers { get; set; }
            public int TotalPoints { get; set; }
            public int EarnedPoints { get; set; }
            public double Percentage { get; set; }
            public string Grade { get; set; }
            public List<QuestionResult> QuestionResults { get; set; }
            public DateTime GradedAt { get; set; }
            public int TimeTaken { get; set; }
        }

        public class QuestionResult
        {
            public int QuestionNumber { get; set; }
            public string QuestionText { get; set; }
            public string StudentAnswer { get; set; }
            public string CorrectAnswer { get; set; }
            public bool IsCorrect { get; set; }
            public int Points { get; set; }
            public int EarnedPoints { get; set; }
            public string QuestionType { get; set; }
        }

        public GradingTool()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SecureExam"
            );

            submissionsDirectory = Path.Combine(appData, "Submissions");
            examsDirectory = Path.Combine(appData, "Exams");

            Directory.CreateDirectory(submissionsDirectory);
            Directory.CreateDirectory(examsDirectory);
        }

        public List<string> GetSubmissionFiles()
        {
            try
            {
                return Directory.GetFiles(submissionsDirectory, "*.json").ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public GradeResult GradeSubmission(string submissionFilePath)
        {
            try
            {
                // Load submission
                string submissionJson = File.ReadAllText(submissionFilePath);
                var submission = JsonSerializer.Deserialize<ExamSubmission>(submissionJson);

                if (submission == null)
                {
                    throw new Exception("Failed to load submission");
                }

                // Load exam
                string examPath = Path.Combine(examsDirectory, $"{submission.ExamId}.json");
                if (!File.Exists(examPath))
                {
                    throw new Exception($"Exam file not found: {submission.ExamId}");
                }

                string examJson = File.ReadAllText(examPath);
                var exam = JsonSerializer.Deserialize<ExamContent>(examJson);

                if (exam == null || exam.Questions == null)
                {
                    throw new Exception("Failed to load exam content");
                }

                // Grade the submission
                var result = new GradeResult
                {
                    StudentId = submission.StudentId,
                    ExamId = submission.ExamId,
                    TotalQuestions = exam.Questions.Count,
                    GradedAt = DateTime.Now,
                    TimeTaken = submission.TimeTaken,
                    QuestionResults = new List<QuestionResult>()
                };

                int correctAnswers = 0;
                int totalPoints = 0;
                int earnedPoints = 0;

                for (int i = 0; i < exam.Questions.Count; i++)
                {
                    var question = exam.Questions[i];
                    totalPoints += question.Points;

                    string studentAnswer = string.Empty;
                    if (submission.Answers != null && submission.Answers.ContainsKey(i))
                    {
                        studentAnswer = submission.Answers[i] ?? string.Empty;
                    }

                    bool isCorrect = CheckAnswer(question, studentAnswer);
                    int pointsEarned = isCorrect ? question.Points : 0;

                    if (isCorrect)
                    {
                        correctAnswers++;
                        earnedPoints += question.Points;
                    }

                    result.QuestionResults.Add(new QuestionResult
                    {
                        QuestionNumber = i + 1,
                        QuestionText = question.Text ?? string.Empty,
                        StudentAnswer = studentAnswer,
                        CorrectAnswer = question.CorrectAnswer ?? string.Empty,
                        IsCorrect = isCorrect,
                        Points = question.Points,
                        EarnedPoints = pointsEarned,
                        QuestionType = question.Type ?? "UNKNOWN"
                    });
                }

                result.CorrectAnswers = correctAnswers;
                result.TotalPoints = totalPoints;
                result.EarnedPoints = earnedPoints;
                result.Percentage = totalPoints > 0 ? (double)earnedPoints / totalPoints * 100 : 0;
                result.Grade = CalculateGrade(result.Percentage);

                // Save grading result
                SaveGradingResult(result);

                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error grading submission: {ex.Message}");
            }
        }

        private bool CheckAnswer(Question question, string studentAnswer)
        {
            if (string.IsNullOrWhiteSpace(studentAnswer))
                return false;

            if (question.Type == "MCQ")
            {
                // For MCQ, exact match required
                return NormalizeAnswer(studentAnswer) == NormalizeAnswer(question.CorrectAnswer ?? string.Empty);
            }
            else if (question.Type == "SHORT")
            {
                // For short answers, check for keyword matches
                string normalizedStudent = NormalizeAnswer(studentAnswer);
                string normalizedCorrect = NormalizeAnswer(question.CorrectAnswer ?? string.Empty);

                // Exact match
                if (normalizedStudent == normalizedCorrect)
                    return true;

                // Keyword matching (at least 70% of keywords present)
                var correctKeywords = normalizedCorrect.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var studentKeywords = normalizedStudent.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (correctKeywords.Length == 0)
                    return false;

                int matchCount = correctKeywords.Count(kw => studentKeywords.Contains(kw));
                double matchPercentage = (double)matchCount / correctKeywords.Length;

                return matchPercentage >= 0.7;
            }
            else if (question.Type == "LONG")
            {
                // For long answers, require manual grading
                // For now, just check if answer is provided
                return !string.IsNullOrWhiteSpace(studentAnswer);
            }

            return false;
        }

        private string NormalizeAnswer(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return string.Empty;

            return answer.Trim().ToLower()
                .Replace("\n", " ")
                .Replace("\r", "")
                .Replace("  ", " ");
        }

        private string CalculateGrade(double percentage)
        {
            if (percentage >= 90) return "A+";
            if (percentage >= 85) return "A";
            if (percentage >= 80) return "A-";
            if (percentage >= 75) return "B+";
            if (percentage >= 70) return "B";
            if (percentage >= 65) return "B-";
            if (percentage >= 60) return "C+";
            if (percentage >= 55) return "C";
            if (percentage >= 50) return "C-";
            if (percentage >= 45) return "D";
            return "F";
        }

        private void SaveGradingResult(GradeResult result)
        {
            try
            {
                string gradesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Grades"
                );
                Directory.CreateDirectory(gradesDir);

                string filename = $"{result.StudentId}_{result.ExamId}_grade_{DateTime.Now:yyyyMMddHHmmss}.json";
                string filepath = Path.Combine(gradesDir, filename);

                string json = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(filepath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving grade: {ex.Message}");
            }
        }

        public string GenerateGradeReport(GradeResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine("             EXAM GRADE REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"Student ID:      {result.StudentId}");
            sb.AppendLine($"Exam ID:         {result.ExamId}");
            sb.AppendLine($"Graded At:       {result.GradedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Time Taken:      {result.TimeTaken} minutes");
            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine("                  SUMMARY");
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine($"Total Questions: {result.TotalQuestions}");
            sb.AppendLine($"Correct Answers: {result.CorrectAnswers}");
            sb.AppendLine($"Total Points:    {result.TotalPoints}");
            sb.AppendLine($"Earned Points:   {result.EarnedPoints}");
            sb.AppendLine($"Percentage:      {result.Percentage:F2}%");
            sb.AppendLine($"Grade:           {result.Grade}");
            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine("              DETAILED RESULTS");
            sb.AppendLine("───────────────────────────────────────────────────────");

            foreach (var qr in result.QuestionResults)
            {
                sb.AppendLine();
                sb.AppendLine($"Question {qr.QuestionNumber} ({qr.QuestionType}):");
                sb.AppendLine($"  {qr.QuestionText}");
                sb.AppendLine($"  Student Answer:  {qr.StudentAnswer}");
                sb.AppendLine($"  Correct Answer:  {qr.CorrectAnswer}");
                sb.AppendLine($"  Result:          {(qr.IsCorrect ? "✓ CORRECT" : "✗ INCORRECT")}");
                sb.AppendLine($"  Points:          {qr.EarnedPoints}/{qr.Points}");
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine(GenerateRecommendations(result));
            sb.AppendLine("═══════════════════════════════════════════════════════");

            return sb.ToString();
        }

        private string GenerateRecommendations(GradeResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PERFORMANCE RECOMMENDATIONS:");

            if (result.Percentage >= 90)
            {
                sb.AppendLine("✓ Excellent performance! Keep up the great work.");
            }
            else if (result.Percentage >= 75)
            {
                sb.AppendLine("✓ Good performance. Review questions you missed for improvement.");
            }
            else if (result.Percentage >= 60)
            {
                sb.AppendLine("⚠ Satisfactory performance. More practice recommended.");
            }
            else
            {
                sb.AppendLine("⚠ Needs improvement. Please review the material thoroughly.");
            }

            // Analyze question types
            var mcqCorrect = result.QuestionResults.Count(q => q.QuestionType == "MCQ" && q.IsCorrect);
            var mcqTotal = result.QuestionResults.Count(q => q.QuestionType == "MCQ");

            if (mcqTotal > 0)
            {
                double mcqPercentage = (double)mcqCorrect / mcqTotal * 100;
                sb.AppendLine($"• Multiple Choice: {mcqCorrect}/{mcqTotal} ({mcqPercentage:F1}%)");
            }

            var shortCorrect = result.QuestionResults.Count(q => q.QuestionType == "SHORT" && q.IsCorrect);
            var shortTotal = result.QuestionResults.Count(q => q.QuestionType == "SHORT");

            if (shortTotal > 0)
            {
                double shortPercentage = (double)shortCorrect / shortTotal * 100;
                sb.AppendLine($"• Short Answer: {shortCorrect}/{shortTotal} ({shortPercentage:F1}%)");
            }

            return sb.ToString();
        }

        public void ExportGradeReport(GradeResult result, string filepath)
        {
            try
            {
                string report = GenerateGradeReport(result);
                File.WriteAllText(filepath, report);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error exporting report: {ex.Message}");
            }
        }

        public List<GradeResult> GetAllGrades()
        {
            var grades = new List<GradeResult>();

            try
            {
                string gradesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureExam",
                    "Grades"
                );

                if (!Directory.Exists(gradesDir))
                    return grades;

                var gradeFiles = Directory.GetFiles(gradesDir, "*_grade_*.json");

                foreach (var file in gradeFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var grade = JsonSerializer.Deserialize<GradeResult>(json);
                        if (grade != null)
                        {
                            grades.Add(grade);
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
                Console.WriteLine($"Error loading grades: {ex.Message}");
            }

            return grades;
        }

        public GradeResult GetStudentGrade(string studentId, string examId)
        {
            var allGrades = GetAllGrades();
            return allGrades
                .Where(g => g.StudentId == studentId && g.ExamId == examId)
                .OrderByDescending(g => g.GradedAt)
                .FirstOrDefault();
        }
    }
}
