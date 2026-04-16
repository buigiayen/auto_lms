using System.Collections.Generic;
using Newtonsoft.Json;

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

    public class CourseResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("result")]
        public CourseResponseResult? Result { get; set; }
    }

    public class CourseResponseResult
    {
        [JsonProperty("iid")]
        public int Iid { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("ntype")]
        public string? Ntype { get; set; }
    }

    public class RubricItem
    {
        [JsonProperty("iid")]
        public int? Iid { get; set; }

        [JsonProperty("practice_id")]
        public int? PracticeId { get; set; }

        [JsonProperty("practice_iid")]
        public int? PracticeIid { get; set; }

        [JsonProperty("ref_iid")]
        public int? RefIid { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("score")]
        public double? Score { get; set; }

        [JsonProperty("passed")]
        public int? Passed { get; set; }

        [JsonProperty("max_score")]
        public double? MaxScore { get; set; }

        [JsonProperty("raw_score")]
        public List<double>? RawScore { get; set; }
    }

    public class RubricData
    {
        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("max_score")]
        public double? MaxScore { get; set; }

        [JsonProperty("pass_score")]
        public double? PassScore { get; set; }
    }

    public class RubricResult
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("deadline")]
        public string? Deadline { get; set; }

        [JsonProperty("end_date")]
        public long? EndDate { get; set; }

        [JsonProperty("score")]
        public double? Score { get; set; }

        [JsonProperty("p")]
        public double? P { get; set; }

        [JsonProperty("cp")]
        public double? Cp { get; set; }

        [JsonProperty("passed")]
        public int? Passed { get; set; }

        [JsonProperty("rubric")]
        public RubricData? Rubric { get; set; }

        [JsonProperty("__expand")]
        public RubricExpandContainer? Expand { get; set; }
    }

    public class RubricExpandContainer
    {
        [JsonProperty("score_by_rubric")]
        public ScoreByRubricData? ScoreByRubric { get; set; }
    }

    public class ScoreByRubricData
    {
        [JsonProperty("score")]
        public double? Score { get; set; }

        [JsonProperty("cp")]
        public double? Cp { get; set; }

        [JsonProperty("passed")]
        public int? Passed { get; set; }

        [JsonProperty("score_by_rubrik")]
        public List<RubricItem>? Items { get; set; }
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
    public class KpiData
    {
        public double GainedTime { get; set; }
        public double RequiredTime { get; set; }
        public double Percent { get; set; }
        public bool IsReached { get; set; }
        public string FormattedProgress { get; set; } = "0/0 giờ";

        public static KpiData Empty => new KpiData { FormattedProgress = "0/0 giờ", Percent = 0 };
    }
}
