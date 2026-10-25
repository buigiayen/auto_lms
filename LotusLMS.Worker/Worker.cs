using Microsoft.Extensions.Options;
using LotusLMS.Api;

namespace LotusLMS.Worker;

internal sealed class Worker(
    ILogger<Worker> logger,
    IOptions<WorkerOptions> options,
    IOptions<LotusLmsCredentialsOptions> credentials,
    IServiceProvider services,
    IHostApplicationLifetime applicationLifetime,
    ClientWorkerBridge? clientBridge = null) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;
    private readonly TimeSpan _statusInterval =
        TimeSpan.FromSeconds(options.Value.StatusIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteJobAsync(stoppingToken);
            clientBridge?.SetOutcome(stoppingToken.IsCancellationRequested ? "stopped" : Environment.ExitCode == 0 ? "completed" : "failed");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { clientBridge?.SetOutcome("stopped"); }
        catch { clientBridge?.SetOutcome("failed"); throw; }
        finally { applicationLifetime.StopApplication(); }
    }

    private async Task ExecuteJobAsync(CancellationToken stoppingToken)
    {
        if (clientBridge is not null)
        {
            await clientBridge.WaitForPermissionAsync(stoppingToken);
        }

        if (_options.Mode == WorkerMode.ReadOnlyProbe)
        {
            await ExecuteReadOnlyProbeAsync(clientBridge?.Credentials ?? credentials.Value, stoppingToken);
            return;
        }

        if (_options.Mode == WorkerMode.PracticeOnly)
        {
            await ExecutePracticeOnlyAsync(clientBridge?.Credentials ?? credentials.Value, stoppingToken);
            return;
        }

        if (_options.Mode == WorkerMode.AutoStudy)
        {
            await ExecuteAutoStudyAsync(clientBridge?.Credentials ?? credentials.Value, stoppingToken);
            return;
        }

        logger.LogInformation(
            "LotusLMS worker is idle. No study job is started automatically.");

        using var timer = new PeriodicTimer(_statusInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                logger.LogDebug("LotusLMS worker heartbeat at {TimestampUtc}.", DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("LotusLMS worker is stopping.");
        }
    }

    private async Task ExecuteReadOnlyProbeAsync(
        LotusLmsCredentialsOptions accountCredentials,
        CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Starting a bounded LotusLMS read-only probe. No learning-time or practice mutation will be sent.");

        try
        {
            var report = await services.GetRequiredService<LotusLmsReadOnlyProbe>().RunAsync(
                accountCredentials,
                _options,
                stoppingToken);

            logger.LogInformation(
                "Read-only probe succeeded at {ObservedAtUtc}: {PlanCount} enrolment plans, " +
                "{CourseCount} unique courses, {WarningCount} contract warnings.",
                report.ObservedAtUtc,
                report.Plans.Count,
                report.CourseCount,
                report.WarningCount);

            foreach (var plan in report.Plans)
            {
                logger.LogInformation(
                    "Enrolment plan {EnrolmentPlanIid}: {CourseCount} courses from {RowCount} rows; " +
                    "truncated={Truncated}.",
                    plan.EnrolmentPlanIid,
                    plan.Courses.Count,
                    plan.CourseRowsReturned,
                    plan.CourseResultsTruncated);

                foreach (var course in plan.Courses)
                {
                    logger.LogInformation(
                        "Course {CourseIid}: {CourseName}; completion={CompletionPercent}; " +
                        "learningHours={LearningHours}; completed={Completed}.",
                        course.CourseIid,
                        course.Name,
                        course.CompletionPercent,
                        course.LearningHours,
                        course.Completed);
                }
            }

            if (report.PlanResultsTruncated)
            {
                logger.LogWarning("Enrolment plan results reached the configured page limit.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Read-only probe was cancelled.");
        }
        catch (LotusLmsApiException exception)
        {
            Environment.ExitCode = 1;
            logger.LogError(
                "Read-only probe failed at API path {ApiPath} with HTTP status {StatusCode}. " +
                "Server message: {ServerMessage}. No response body or session data was logged.",
                SafeApiPath(exception.Endpoint),
                exception.StatusCode,
                SafeLogText(exception.ServerMessage));
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            logger.LogError(
                "Read-only probe failed with {ErrorType}: {SafeMessage}",
                exception.GetType().Name,
                exception.Message);
        }
    }

    private async Task ExecuteAutoStudyAsync(
        LotusLmsCredentialsOptions accountCredentials,
        CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Starting a bounded LotusLMS auto-study job. It studies incomplete videos " +
            "in course order; SCO grouping nodes never receive learning-time saves.");

        try
        {
            var report = await services.GetRequiredService<LotusLmsAutoStudy>().RunAsync(
                accountCredentials,
                _options,
                stoppingToken);

            logger.LogInformation(
                "Auto-study finished: {PlanCount} plans, {CourseCount} courses, " +
                "{SaveCount} learning-time saves, {TrackCount} track-time calls; " +
                "dailyKpiReached={DailyKpiReached}.",
                report.PlansProcessed,
                report.CoursesProcessed,
                report.LearningTimeSaves,
                report.TrackTimeCalls,
                report.DailyKpiReached);

            foreach (var course in report.Courses)
            {
                logger.LogInformation(
                    "Course {CourseIid} '{CourseName}': saves={Saves}, tracks={Tracks}, " +
                    "itemsCompleted={ItemsCompleted}, kpiReached={KpiReached}, practice='{Practice}'.",
                    course.CourseIid,
                    course.Name,
                    course.LearningTimeSaves,
                    course.TrackTimeCalls,
                    course.ItemsCompleted,
                    course.DailyKpiReached,
                    course.PracticeOutcome ?? "(skipped)");

                foreach (var warning in course.Warnings)
                {
                    logger.LogWarning("Course {CourseIid}: {Warning}", course.CourseIid, warning);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Auto-study was cancelled.");
        }
        catch (LotusLmsApiException exception)
        {
            Environment.ExitCode = 1;
            logger.LogError(
                "Auto-study failed at API path {ApiPath} with HTTP status {StatusCode}. " +
                "Server message: {ServerMessage}. No response body or session data was logged.",
                SafeApiPath(exception.Endpoint),
                exception.StatusCode,
                SafeLogText(exception.ServerMessage));
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            logger.LogError(
                "Auto-study failed with {ErrorType}: {SafeMessage}",
                exception.GetType().Name,
                exception.Message);
        }
    }

    private async Task ExecutePracticeOnlyAsync(
        LotusLmsCredentialsOptions accountCredentials,
        CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Starting practice-only: {BatchSize} questions per batch, random submission intervals of {MinIntervalSeconds}-{MaxIntervalSeconds}s.",
            _options.PracticeBatchSize, _options.PracticeIntervalSeconds, _options.PracticeMaxIntervalSeconds);

        try
        {
            var report = await services.GetRequiredService<LotusLmsPracticeOnly>().RunAsync(accountCredentials, _options, stoppingToken);
            logger.LogInformation(
                "Practice-only finished: {CourseCount} courses, " +
                "{QuestionCount} questions submitted in {BatchCount} batches; KPI gained={GainedKpiSeconds}s.",
                report.CoursesProcessed,
                report.QuestionsSubmitted, report.BatchesSubmitted, report.GainedKpiSeconds);
            foreach (var course in report.Courses)
            {
                logger.LogInformation(
                    "Practice-only course {CourseIid} '{CourseName}': {Outcome}.",
                    course.CourseIid, course.Name, course.Result.Outcome);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Practice-only was cancelled.");
        }
        catch (LotusLmsApiException exception)
        {
            Environment.ExitCode = 1;
            logger.LogError(
                "Practice-only failed at API path {ApiPath} with HTTP status {StatusCode}. " +
                "Server message: {ServerMessage}.",
                SafeApiPath(exception.Endpoint), exception.StatusCode,
                SafeLogText(exception.ServerMessage));
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            logger.LogError(
                "Practice-only failed with {ErrorType}: {SafeMessage}",
                exception.GetType().Name, exception.Message);
        }
    }

    private static string SafeApiPath(string endpoint)
    {
        var queryIndex = endpoint.IndexOf('?', StringComparison.Ordinal);
        return queryIndex < 0 ? endpoint : endpoint[..queryIndex];
    }

    private static string SafeLogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        var normalized = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }
}
