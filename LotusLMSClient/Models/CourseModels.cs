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
    }
}
