using System.Collections.Generic;

namespace LotusLMSClient.Models
{
    public class Course
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string InstructorName { get; set; }
        public int CompletedLessons { get; set; }
        public int TotalLessons { get; set; }
        public double ProgressPercent { get; set; }
        public string Status { get; set; } // "Learning", "Completed", "NotStarted"
        public List<Lesson> Lessons { get; set; }
    }

    public class Lesson
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ContentType { get; set; } // "Video", "PDF", "Html"
        public string ContentUrl { get; set; }
        public bool IsCompleted { get; set; }
    }
}
