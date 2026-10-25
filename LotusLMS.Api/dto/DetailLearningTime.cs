namespace LotusLMS.Api;
/// <summary>
/// danh sách chi tiese thời gian học tập của từng bài học trong khóa học
/// </summary>
public sealed class ResultDetailLearningTime
{
    public string? Id { get; set; }
    public int Iid { get; set; }
    public string? Name { get; set; }
    public string? Ntype { get; set; }
    public int MinLearningTime { get; set; }
    public List<LearningTimeNode> Children { get; set; } = [];
    public double ActualLearningTime => Children.Sum(c => c.TotalActualLearningTime);
    public bool IsSuccess => ActualLearningTime >= MinLearningTime;
}

public sealed class LearningTimeNode
{
    public string? Id { get; set; }
    public int Iid { get; set; }
    public string? Ntype { get; set; }
    public string? Name { get; set; }
    public List<LearningTimeNode> Children { get; set; } = [];
    public int MinLearningTime { get; set; }
    public double ActualLearningTime { get; set; }
    public bool IsLeaf => Children.Count == 0;

    // Sum leaf times recursively to avoid counting parent aggregates twice.
    public double TotalActualLearningTime => IsLeaf
        ? ActualLearningTime
        : Children.Sum(c => c.TotalActualLearningTime);

    public bool IsSuccessLearningTime => ActualLearningTime >= MinLearningTime;
}
