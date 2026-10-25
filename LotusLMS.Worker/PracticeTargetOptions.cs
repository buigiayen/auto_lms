namespace LotusLMS.Worker;

/// <summary>Practice location; session metadata is resolved from the API on every run.</summary>
public sealed class PracticeTargetOptions
{
    public int CourseIid { get; init; }
    public int PracticeIid { get; init; }
    // Legacy configuration is still accepted, but never used to create a new take.
    public int ExamScoIid { get; init; }
    public int BankIid { get; init; }
    public string TakeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    internal bool IsValid => CourseIid > 0 && PracticeIid > 0;
}
