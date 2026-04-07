using System.Collections.Generic;

namespace LotusLMSClient.Models
{
    public class Course
    {
        public int Id { get; set; }
        public int Iid { get; set; }
        public string Name { get; set; }
        public string InstructorName { get; set; }
        public int CompletedLessons { get; set; }
        public int TotalLessons { get; set; }
        public double ProgressPercent { get; set; }
        public string Status { get; set; } // "Learning", "Completed", "NotStarted"
    }

    public class RubricItem
    {
        public int? Iid { get; set; }
        public string? Name { get; set; }
        public double? Score { get; set; }
        public int? Passed { get; set; }
        public double? MaxScore { get; set; }
        public List<double>? RawScore { get; set; }
    }

    public class RubricData
    {
        public string? Description { get; set; }
        public double? MaxScore { get; set; }
        public double? PassScore { get; set; }
    }

    public class RubricResult
    {
        public double? Score { get; set; }
        public double? Cp { get; set; }
        public int? Passed { get; set; }
        public RubricData? Rubric { get; set; }
        public List<RubricItem>? ScoreByRubrik { get; set; }
    }

    public class RubricResponse
    {
        public bool Success { get; set; }
        public RubricResult? Result { get; set; }
    }

    public class LearningTimeItem
    {
        public string? Name { get; set; }
        public double? Time { get; set; }
        public string? Type { get; set; }
    }

    public class LearningTimeResult
    {
        public List<LearningTimeItem>? Items { get; set; }
    }

    public class LearningTimeResponse
    {
        public bool Success { get; set; }
        public LearningTimeResult? Result { get; set; }
    }

    public class LearningTreeNode
    {
        public string Name { get; set; }
        public List<LearningTreeNode> Children { get; set; } = new List<LearningTreeNode>();
    }
}
