namespace LotusLMS.Worker;

internal sealed record AutoStudyReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    int PlansProcessed,
    int CoursesProcessed,
    int LearningTimeSaves,
    int TrackTimeCalls,
    bool DailyKpiReached,
    IReadOnlyList<AutoStudyCourseResult> Courses);

internal sealed record AutoStudyCourseResult(
    int EnrolmentPlanIid,
    int CourseIid,
    string Name,
    int LearningTimeSaves,
    int TrackTimeCalls,
    int ItemsCompleted,
    bool DailyKpiReached,
    string? PracticeOutcome,
    IReadOnlyList<string> Warnings);
