namespace LotusLMS.Worker;

public enum WorkerMode
{
    Idle,
    ReadOnlyProbe,
    AutoStudy
}

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public WorkerMode Mode { get; init; } = WorkerMode.Idle;

    public int StatusIntervalSeconds { get; init; } = 60;

    public int PlanPageSize { get; init; } = 12;

    public int CoursePageSize { get; init; } = 20;

    public int MaxPages { get; init; } = 100;

    // Auto-study: seconds sent per save-item-learning-time call (client uses 15).
    public int SaveItemSeconds { get; init; } = 15;

    // Auto-study: seconds sent to track-time.
    public int TrackTimeSeconds { get; init; } = 15;

    // Auto-study: when StopOnDailyKpi is enabled, check daily KPI and send
    // track-time every N learning-time saves (client uses 3).
    public int KpiCheckEverySaves { get; init; } = 3;

    // Auto-study: optionally stop the whole job as soon as the daily KPI is reached.
    // Disabled by default so completed courses advance to the next course in order.
    public bool StopOnDailyKpi { get; init; }

    // Auto-study: run the practice auto-complete step as well. Practice is outside
    // the sequential video-learning flow and is disabled by default.
    public bool IncludePractice { get; init; }

    // Auto-study: number of questions submitted per save-take batch (client uses 5).
    public int PracticeBatchSize { get; init; } = 5;

    // Auto-study: mark the last practice batch as finished. The current web client
    // always submits finished=0, so this stays disabled by default for parity.
    public bool MarkPracticeFinished { get; init; }

    // Auto-study: only process the first enrolment plan (matches the web client).
    public bool OnlyFirstPlan { get; init; } = true;

    // Auto-study: only read page 1 of courses (matches the web client).
    public bool OnlyFirstCoursePage { get; init; } = true;
}
