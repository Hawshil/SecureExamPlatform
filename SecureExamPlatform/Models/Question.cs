using System.Collections.Generic;

namespace SecureExamPlatform.Models
{
    public class Question
    {
        public string Text { get; set; }
        public string Type { get; set; } // MCQ, SHORT, LONG
        public List<string> Options { get; set; }
        public string CorrectAnswer { get; set; }
        public int Points { get; set; }
    }
}
