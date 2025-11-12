using System;
using System.Collections.Generic;

namespace SecureExamPlatform.Models
{
    public class ExamSubmission
    {
        public string StudentId { get; set; }
        public string ExamId { get; set; }
        public string LabId { get; set; }
        public Dictionary<int, string> Answers { get; set; }
        public DateTime SubmittedAt { get; set; }
        public int TimeTaken { get; set; } // in minutes
    }
}
