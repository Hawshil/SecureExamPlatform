using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureExamPlatform.Models
{
    public class ExamSubmission
    {
        public string SessionId { get; set; }
        public string StudentId { get; set; }
        public string ExamId { get; set; }
        public Dictionary<string, string> Answers { get; set; }
        public DateTime SubmissionTime { get; set; }
        public TimeSpan TimeSpent { get; set; }
    }
}