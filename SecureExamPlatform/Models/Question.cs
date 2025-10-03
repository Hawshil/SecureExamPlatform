using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureExamPlatform.Models
{
    /// <summary>
    /// Represents the type of a question.
    /// </summary>
    public enum QuestionType
    {
        Essay,
        MultipleChoice,
        TrueFalse,
        FillInTheBlank
    }

    /// <summary>
    /// Represents a single question in an exam.
    /// </summary>
    public class Question
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public QuestionType Type { get; set; }

        // For MultipleChoice or TrueFalse questions
        public List<string> Options { get; set; } = new List<string>();

        // The correct answer (can be an index for MCQ or text for other types)
        public string CorrectAnswer { get; set; }

        // How many points the question is worth
        public int Points { get; set; }
    }
}