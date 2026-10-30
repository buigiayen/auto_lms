using Microsoft.Extensions.Options;

namespace LotusLMS.Worker.V2;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";
    public string WorkerName { get; set; } = "";
    public string? ServerName { get; init; }
    public int MaxConcurrentAccounts { get; init; } = 5;
    public string RegistrationApiKey { get; init; } = "";
    public int PollIntervalSeconds { get; init; }
    public int HeartbeatIntervalSeconds { get; init; }
    public int StatusIntervalSeconds { get; init; } = 60;
    public bool WorkRestEnabled { get; init; } = true;
    public int ContinuousWorkMinutes { get; init; } = 180;
    public int MinRestMinutes { get; init; } = 15;
    public int MaxRestMinutes { get; init; } = 30;

    internal int PickRestSeconds() => Random.Shared.Next(MinRestMinutes * 60, MaxRestMinutes * 60 + 1);

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

    // Auto-study: defer the current account until tomorrow after the daily KPI is reached.
    // Disabled by default so completed courses advance to the next course in order.
    public bool StopOnDailyKpi { get; init; }

    // Auto-study: run the practice auto-complete step as well. Practice is outside
    // the sequential video-learning flow and is disabled by default.
    public bool IncludePractice { get; init; }

    // Practice: number of questions submitted per save-take batch (client uses 5).
    public int PracticeBatchSize { get; init; } = 5;

    // Inclusive bounds for a new random interval before every practice submission.
    public int PracticeIntervalSeconds { get; init; } = 5;
    public int PracticeMaxIntervalSeconds { get; init; } = 10;

    // Practice: mark the last practice batch as finished. The current web client
    // always submits finished=0, so this stays disabled by default for parity.
    public bool MarkPracticeFinished { get; init; }

    // Job scope: only process the first enrolment plan (matches the web client).
    public bool OnlyFirstPlan { get; init; } = true;

    // Job scope: only read page 1 of courses (matches the web client).
    public bool OnlyFirstCoursePage { get; init; } = true;

     internal bool IsValid() => !ValidationErrors().Any();

    internal IEnumerable<string> ValidationErrors()
    {
        var workerNameIsValid = !string.IsNullOrWhiteSpace(WorkerName)
            && WorkerName == WorkerName.Trim()
            && WorkerName.Length <= 96;
        if (!workerNameIsValid)
            yield return "Worker:WorkerName phải dài 1–96 ký tự, không có khoảng trắng ở hai đầu.";
        if (workerNameIsValid && GetLaneWorkerName(1).Length > 100)
            yield return "Tổng tên lane từ Worker:ServerName và Worker:WorkerName không được vượt quá 100 ký tự, gồm cả dấu nối và số thứ tự.";
        if (MaxConcurrentAccounts is < 1 or > 20)
            yield return "Worker:MaxConcurrentAccounts phải từ 1 đến 20.";
        if (RegistrationApiKey.Length < 32)
            yield return "Worker:RegistrationApiKey cần secret bootstrap tối thiểu 32 ký tự, được cấu hình giống WORKER_REGISTRATION_KEY trên Client API.";
        if (PollIntervalSeconds is < 1 or > 60)
            yield return "Worker:PollIntervalSeconds phải từ 1 đến 60 giây.";
        if (HeartbeatIntervalSeconds is < 1 or > 10)
            yield return "Worker:HeartbeatIntervalSeconds phải từ 1 đến 10 giây.";
        if (WorkRestEnabled)
        {
            if (ContinuousWorkMinutes is < 1 or > 1440)
                yield return "Worker:ContinuousWorkMinutes phải từ 1 đến 1440 phút.";
            if (MinRestMinutes is < 1 or > 1440)
                yield return "Worker:MinRestMinutes phải từ 1 đến 1440 phút.";
            if (MaxRestMinutes < MinRestMinutes || MaxRestMinutes > 1440)
                yield return "Worker:MaxRestMinutes phải lớn hơn hoặc bằng MinRestMinutes và không vượt quá 1440 phút.";
        }
    }

    private string EffectiveServerName => string.IsNullOrWhiteSpace(ServerName)
        ? Environment.MachineName
        : ServerName.Trim();

    internal string GetLaneWorkerName(int lane) => $"{EffectiveServerName}-{WorkerName}-{lane:00}";
}

internal sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        var errors = options.ValidationErrors().ToArray();
        return errors.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
