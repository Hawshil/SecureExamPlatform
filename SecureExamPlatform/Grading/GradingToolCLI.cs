using System;
using System.Linq;

namespace SecureExamPlatform.Grading
{
    public class GradingToolCLI
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine("      Secure Exam Platform - Grading Tool CLI");
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine();

            var gradingTool = new GradingTool();
            var submissions = gradingTool.GetSubmissionFiles();

            if (submissions.Count == 0)
            {
                Console.WriteLine("No submissions found.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine($"Found {submissions.Count} submission(s):");
            Console.WriteLine();

            for (int i = 0; i < submissions.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {System.IO.Path.GetFileName(submissions[i])}");
            }

            Console.WriteLine();
            Console.Write("Enter submission number to grade (or 'all' to grade all): ");
            string input = Console.ReadLine();

            if (input.ToLower() == "all")
            {
                Console.WriteLine();
                Console.WriteLine("Grading all submissions...");
                Console.WriteLine();

                foreach (var submission in submissions)
                {
                    GradeSubmissionFile(gradingTool, submission);
                }
            }
            else if (int.TryParse(input, out int index) && index > 0 && index <= submissions.Count)
            {
                Console.WriteLine();
                GradeSubmissionFile(gradingTool, submissions[index - 1]);
            }
            else
            {
                Console.WriteLine("Invalid input.");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static void GradeSubmissionFile(GradingTool gradingTool, string submissionFile)
        {
            try
            {
                Console.WriteLine($"Grading: {System.IO.Path.GetFileName(submissionFile)}");

                var result = gradingTool.GradeSubmission(submissionFile);

                Console.WriteLine($"✓ Graded successfully!");
                Console.WriteLine($"  Student: {result.StudentId}");
                Console.WriteLine($"  Score: {result.EarnedPoints}/{result.TotalPoints} ({result.Percentage:F2}%)");
                Console.WriteLine($"  Grade: {result.Grade}");
                Console.WriteLine();

                // Generate report
                string report = gradingTool.GenerateGradeReport(result);
                string reportPath = submissionFile.Replace(".json", "_report.txt");
                gradingTool.ExportGradeReport(result, reportPath);
                Console.WriteLine($"  Report saved: {System.IO.Path.GetFileName(reportPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
            }
        }
    }
}
