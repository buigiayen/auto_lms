using System.Text.Json.Serialization;

namespace LotusLMS.Api;

/// <summary>
/// Danh mục các bài học học viên sẽ phải tham gia để hoàn thành
/// </summary>
public sealed class Student_course_in_enrolment_plan_view
{
    public string? Name { get; set; }
    public string? Iid { get; set; }
    public Course? Course { get; set; }
    public List<Student_course_in_enrolment_plan_view> Children { get; set; } = [];

}

public sealed class Course
{
    public string? Id { get; set; }
    public string? Slug { get; set; }
    public int Iid { get; set; }
    public string? Name { get; set; }
    public string? Ntype { get; set; }
    public int Cp { get; set; }
    public double P { get; set; }
    public int Pf { get; set; }
    public int Active { get; set; }
    public string? Status { get; set; }
    [JsonPropertyName("__expand")]
    public _Expand? __Expand { get; set; }
    public bool IsPass => __Expand?.ScoreByRubric?.Passed ?? false;

}

public class _Expand
{
    public ScoreByRubric? ScoreByRubric { get; set; }
}

public class ScoreByRubric
{
    public string? Iid { get; set; }
    public Rubric? Rubric { get; set; }
    public double? Score { get; set; }
    public bool? Passed { get; set; }

}


public class Rubric
{
    /// <summary>
    /// Điểm số hiện tại
    /// </summary>
    public double PassScore { get; set; }
    /// <summary>
    /// DDierm số tối đa
    /// </summary>
    public double MaxScore { get; set; }
    public bool? PassPercent => MaxScore > 0 ? (PassScore / MaxScore * 100) > 70 ? true : false : false;

}
