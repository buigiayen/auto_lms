using Microsoft.Extensions.Options;
using LotusLMS.Api;

namespace LotusLMS.Worker;

internal sealed class Worker(
    ILogger<Worker> logger,
    IOptions<WorkerOptions> options,
    IOptions<LotusLmsCredentialsOptions> credentials,
    LotusLmsReadOnlyProbe readOnlyProbe,
    LotusLmsAutoStudy autoStudy,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;
    private readonly TimeSpan _statusInterval =
        TimeSpan.FromSeconds(options.Value.StatusIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Mode == WorkerMode.ReadOnlyProbe)
        {
            await ExecuteReadOnlyProbeAsync(credentials.Value, stoppingToken);
            return;
        }

        if (_options.Mode == WorkerMode.AutoStudy)
        {
            await ExecuteAutoStudyAsync(credentials.Value, stoppingToken);
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
            var report = await readOnlyProbe.RunAsync(
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
        finally
        {
            applicationLifetime.StopApplication();
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
            var report = await autoStudy.RunAsync(
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
        finally
        {
            applicationLifetime.StopApplication();
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
