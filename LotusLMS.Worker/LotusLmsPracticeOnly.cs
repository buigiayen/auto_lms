namespace LotusLMS.Worker;

/// <summary>Standalone practice job with fresh metadata and a server-created take.</summary>
internal sealed class LotusLmsPracticeOnly(
    LotusLmsAccountReader accountReader,
    LotusLmsPractice practice,
    TimeProvider timeProvider,
    ILogger<LotusLmsPracticeOnly> logger)
{
    public async Task<PracticeReport> RunAsync(
        LotusLmsCredentialsOptions credentials,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(options);

        var startedAt = timeProvider.GetUtcNow();
        var session = await accountReader.LoginAsync(credentials, cancellationToken);
        logger.LogInformation("Practice-only logged in as {LoginName}.", session.LoginName);

        var courses = new List<PracticeCourseReport>();
        var planIds = await accountReader.ReadPlanIdsAsync(session, options, cancellationToken);
        foreach (var planId in planIds)
        {
            logger.LogInformation("Found enrolment plan {PlanId}.", planId);
            courses.AddRange(await practice.RunTargetAsync(session, planId, options, cancellationToken));
        }

        return new PracticeReport(startedAt, timeProvider.GetUtcNow(), courses);
    }
}
