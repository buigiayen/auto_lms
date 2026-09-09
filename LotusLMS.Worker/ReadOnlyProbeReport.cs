namespace LotusLMS.Worker;

internal sealed record ReadOnlyProbeReport(
    DateTimeOffset ObservedAtUtc,
    int PlanRowsReturned,
    IReadOnlyList<PlanProbeResult> Plans,
    bool PlanResultsTruncated)
{
    public int CourseCount => Plans.Sum(plan => plan.Courses.Count);

    public int WarningCount => Plans.Sum(plan => plan.Warnings.Count);
}

internal sealed record PlanProbeResult(
    int EnrolmentPlanIid,
    int CourseRowsReturned,
    IReadOnlyList<CourseProbeResult> Courses,
    IReadOnlyList<string> Warnings,
    bool CourseResultsTruncated);

internal sealed record CourseProbeResult(
    int CourseIid,
    string Name,
    double? CompletionPercent,
    double? LearningHours,
    bool? Completed);
