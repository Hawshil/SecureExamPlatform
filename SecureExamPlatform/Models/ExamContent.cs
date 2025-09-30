using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureExamPlatform.Models
{
    public class ExamContent
    {
        public string ExamId { get; set; }
        public string Title { get; set; }
        public TimeSpan Duration { get; set; }
        public List<Question> Questions { get; set; } = new List<Question>();
    }
}
