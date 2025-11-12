using System.Collections.Generic;

namespace SecureExam.Core.Models
{
    public class ExamContent
    {
        public string Title { get; set; }
        public int DurationMinutes { get; set; }
        public List<Question> Questions { get; set; }
    }
}
