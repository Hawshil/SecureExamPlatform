using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;
using SecureExamPlatform.Grading;
using SecureExamPlatform.Models;

namespace SecureExamPlatform.Tests.Grading
{
    public class GradingToolTests : IDisposable
    {
        private readonly GradingTool _gradingTool;
        private readonly string _tempPath;

        public GradingToolTests()
        {
            _gradingTool = new GradingTool();
            _tempPath = Path.Combine(Path.GetTempPath(), "SecureExamTests");
            Directory.CreateDirectory(_tempPath);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempPath, true);
            }
            catch { }
        }

        private string SaveSubmission(ExamSubmission submission)
        {
            var filePath = Path.Combine(_tempPath, $"submission_{submission.StudentId}_{submission.ExamId}.json");
            var json = JsonSerializer.Serialize(submission);
            File.WriteAllText(filePath, json);
            return filePath;
        }

        [Fact]
        public void GradeSubmission_WithValidMCQAnswers_ShouldCalculateCorrectScore()
        {
            // Arrange
            var examContent = new ExamContent
            {
                ExamId = "TEST001",
                Title = "Test Exam",
                Questions = new List<Question>
                {
                    new Question
                    {
                        Id = "Q1",
                        Text = "What is 2+2?",
                        Type = QuestionType.MultipleChoice,
                        Options = new List<string> { "3", "4", "5", "6" },
                        CorrectAnswer = "4",
                        Points = 2
                    },
                    new Question
                    {
                        Id = "Q2",
                        Text = "What is the capital of France?",
                        Type = QuestionType.MultipleChoice,
                        Options = new List<string> { "London", "Paris", "Berlin", "Madrid" },
                        CorrectAnswer = "Paris",
                        Points = 2
                    }
                }
            };

            var submission = new ExamSubmission
            {
                StudentId = "STU001",
                ExamId = "TEST001",
                Answers = new Dictionary<string, string>
                {
                    { "Q1", "1" }, // Index 1 corresponds to "4" (correct)
                    { "Q2", "1" }  // Index 1 corresponds to "Paris" (correct)
                },
                SubmissionTime = DateTime.UtcNow,
                TimeSpent = TimeSpan.FromMinutes(30)
            };

            string submissionPath = SaveSubmission(submission);

            // Act
            var result = _gradingTool.GradeSubmission(submissionPath, examContent);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(4, result.TotalMcqMarks); // Total possible marks (2 questions × 2 points)
            Assert.Equal(4, result.EarnedMcqMarks); // All answers correct
            Assert.Equal(2, result.QuestionsAnswered);
            Assert.Equal(2, result.TotalQuestions);

            // Verify individual question results
            Assert.True(result.QuestionResults["Q1"].IsCorrect);
            Assert.True(result.QuestionResults["Q2"].IsCorrect);
            Assert.Equal(2, result.QuestionResults["Q1"].MarksAwarded);
            Assert.Equal(2, result.QuestionResults["Q2"].MarksAwarded);
        }

        [Fact]
        public void GradeSubmission_WithPartiallyCorrectAnswers_ShouldCalculatePartialScore()
        {
            // Arrange
            var examContent = new ExamContent
            {
                ExamId = "TEST001",
                Title = "Test Exam",
                Questions = new List<Question>
                {
                    new Question
                    {
                        Id = "Q1",
                        Text = "What is 2+2?",
                        Type = QuestionType.MultipleChoice,
                        Options = new List<string> { "3", "4", "5", "6" },
                        CorrectAnswer = "4",
                        Points = 2
                    },
                    new Question
                    {
                        Id = "Q2",
                        Text = "What is the capital of France?",
                        Type = QuestionType.MultipleChoice,
                        Options = new List<string> { "London", "Paris", "Berlin", "Madrid" },
                        CorrectAnswer = "Paris",
                        Points = 2
                    }
                }
            };

            var submission = new ExamSubmission
            {
                StudentId = "STU001",
                ExamId = "TEST001",
                Answers = new Dictionary<string, string>
                {
                    { "Q1", "1" }, // Index 1 corresponds to "4" (correct)
                    { "Q2", "0" }  // Index 0 corresponds to "London" (incorrect)
                },
                SubmissionTime = DateTime.UtcNow,
                TimeSpent = TimeSpan.FromMinutes(30)
            };

            string submissionPath = SaveSubmission(submission);

            // Act
            var result = _gradingTool.GradeSubmission(submissionPath, examContent);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(4, result.TotalMcqMarks); // Total possible marks
            Assert.Equal(2, result.EarnedMcqMarks); // Only one correct answer
            Assert.Equal(2, result.QuestionsAnswered);
            Assert.Equal(2, result.TotalQuestions);

            // Verify individual question results
            Assert.True(result.QuestionResults["Q1"].IsCorrect);
            Assert.False(result.QuestionResults["Q2"].IsCorrect);
            Assert.Equal(2, result.QuestionResults["Q1"].MarksAwarded);
            Assert.Equal(0, result.QuestionResults["Q2"].MarksAwarded);
        }

        [Fact]
        public void GradeSubmission_WithEssayQuestion_ShouldHandleManualGrading()
        {
            // Arrange
            var examContent = new ExamContent
            {
                ExamId = "TEST001",
                Title = "Test Exam",
                Questions = new List<Question>
                {
                    new Question
                    {
                        Id = "Q1",
                        Text = "Explain the theory of relativity.",
                        Type = QuestionType.Essay,
                        Points = 10
                    }
                }
            };

            var submission = new ExamSubmission
            {
                StudentId = "STU001",
                ExamId = "TEST001",
                Answers = new Dictionary<string, string>
                {
                    { "Q1", "Einstein's theory of relativity states that..." }
                },
                SubmissionTime = DateTime.UtcNow,
                TimeSpent = TimeSpan.FromMinutes(30)
            };

            string submissionPath = SaveSubmission(submission);

            // Act
            var result = _gradingTool.GradeSubmission(submissionPath, examContent);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0, result.TotalMcqMarks); // No MCQ questions
            Assert.Equal(10, result.TotalSubjectiveMarks); // Total points for essay
            Assert.Equal(1, result.QuestionsAnswered);
            Assert.Equal(1, result.TotalQuestions);

            // Verify essay question handling
            var questionResult = result.QuestionResults["Q1"];
            Assert.Equal("[Requires Manual Grading]", questionResult.CorrectAnswer);
            Assert.False(questionResult.IsCorrect); // Subjective questions are not automatically marked as correct
            Assert.Equal(0, questionResult.MarksAwarded); // Marks need to be awarded manually
            Assert.Equal(10, questionResult.TotalMarks);
        }
    }
}