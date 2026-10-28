using LotusLMS.Api;
using LotusLMS.Worker.V2.LotusRunner.CheckingTime;
using LotusLMS.Worker2;
using Microsoft.Extensions.Options;

namespace LotusLMS.Worker.V2.LotusRunner.Theory;

public sealed class TheoryServices(
    ILotusLmsApiClient client,
    TimeProvider timeProvider,
    IOptions<WorkerOptions> workerOptions,
    LearningTimeTrackingService learningTimeTrackingService,
    ILogger<TheoryServices> logger)
{
    public async Task<AccountStudyResult> RunAsync(
        LotusLmsSession session,
        IReadOnlyCollection<CheckingTimeDTO.AutoStudy> studyItems,
        Func<Task<StudyPlan>> refreshStudyPlan,
        Func<string, CancellationToken, Task> sendClientLog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(studyItems);
        ArgumentNullException.ThrowIfNull(refreshStudyPlan);
        ArgumentNullException.ThrowIfNull(sendClientLog);
        ValidateOptions(workerOptions.Value);

        var pending = GetTheoryItems(studyItems);
        if (pending.Count > 0)
        {
            const string message = "---- Bắt đầu treo các bài lý thuyết chưa hoàn thành.";
            logger.LogInformation("{Message}", message);
            await sendClientLog(message, cancellationToken);
        }

        var saves = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var savedThisPass = 0;

            foreach (var courseItems in pending.GroupBy(item => (CourseIid: item.iidRoot!.Value, ItemNtype: item.ntype!)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (courseIid, itemNtype) = courseItems.Key;
                var details = await client.GetDetailLearningTimeAsync(
                    session,
                    courseIid,
                    itemNtype,
                    cancellationToken);
                var nodes = Flatten(details.Result?.Children ?? [])
                    .Where(node => string.Equals(node.Ntype, itemNtype, StringComparison.OrdinalIgnoreCase)
                        && node.Iid > 0)
                    .GroupBy(node => node.Iid)
                    .ToDictionary(group => group.Key, group => group.First());

                foreach (var plannedItem in courseItems)
                {

                    cancellationToken.ThrowIfCancellationRequested();
                    if (!nodes.TryGetValue(plannedItem.iid, out var node))
                    {
                        throw new InvalidDataException(
                            $"Theory item {plannedItem.iid} was not returned for course {courseIid}.");
                    }

                    logger.LogInformation(
                        "THEORY_SELECT course={CourseIid} item={ItemIid} name={ItemName} progress={Actual}/{Required}s",
                        courseIid, node.Iid, plannedItem.name, node.ActualLearningTime, node.MinLearningTime);
                    if (node.MinLearningTime <= 0 || node.IsSuccessLearningTime)
                    {
                        continue;
                    }

                    var current = node;
                    while (current.ActualLearningTime < current.MinLearningTime)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var secondsToSave = (int)Math.Ceiling(Math.Min(
                            workerOptions.Value.SaveItemSeconds,
                            current.MinLearningTime - current.ActualLearningTime));

                        await Task.Delay(
                            TimeSpan.FromSeconds(workerOptions.Value.SaveItemSeconds),
                            timeProvider,
                            cancellationToken);

                        var saveId = Guid.NewGuid().ToString("N");
                        var previousActualLearningTime = current.ActualLearningTime;
                        logger.LogInformation(
                            "THEORY_SEND sendId={SendId} course={CourseIid} item={ItemIid} seq={Sequence} seconds={Seconds} before={Before}/{Required}s",
                            saveId, courseIid, current.Iid, saves + 1, secondsToSave,
                            previousActualLearningTime, current.MinLearningTime);
                        await client.SaveItemLearningTimeAsync(
                            session,
                            new SaveItemLearningTimeRequest
                            {
                                ItemIid = current.Iid,
                                ItemNtype = itemNtype,
                                CourseIid = courseIid,
                                TimeSpentSeconds = secondsToSave
                            },
                            cancellationToken);
                        saves++;
                        savedThisPass++;
                        logger.LogInformation(
                            "THEORY_ACK sendId={SendId} course={CourseIid} item={ItemIid} seq={Sequence}",
                            saveId, courseIid, current.Iid, saves);

                        var refreshedDetails = await client.GetDetailLearningTimeAsync(
                            session,
                            courseIid,
                            itemNtype,
                            cancellationToken);
                        current = Flatten(refreshedDetails.Result?.Children ?? [])
                            .FirstOrDefault(item => item.Iid == current.Iid
                                && string.Equals(item.Ntype, itemNtype, StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidDataException(
                                $"Theory item {current.Iid} disappeared after its learning time was saved.");
                        if (current.ActualLearningTime <= previousActualLearningTime)
                        {
                            throw new InvalidDataException(
                                $"Theory item {current.Iid} learning time did not increase after a successful save.");
                        }

                        logger.LogInformation(
                            "THEORY_PROGRESS sendId={SendId} course={CourseIid} item={ItemIid} before={Before} after={Actual} required={Required}s",
                            saveId, courseIid, current.Iid, previousActualLearningTime,
                            current.ActualLearningTime, current.MinLearningTime);

                        if (workerOptions.Value.StopOnDailyKpi
                            && saves % workerOptions.Value.KpiCheckEverySaves == 0)
                        {
                            var kpi = await client.GetTodayKpiTimeAsync(
                                session,
                                courseIid,
                                cancellationToken);
                            if (kpi.TodayGainedKpiTime.Full)
                            {
                                const string message = "Đã đủ KPI trong ngày; tạm dừng xử lý đến ngày mai.";
                                logger.LogInformation("{Message}", message);
                                await sendClientLog(message, cancellationToken);
                                return AccountStudyResult.DailyKpiReached;
                            }

                            await client.TrackTimeAsync(
                                session,
                                new TrackTimeRequest
                                {
                                    CourseIid = courseIid,
                                    TimeSpentSeconds = workerOptions.Value.TrackTimeSeconds,
                                    PageId = "learn"
                                },
                                cancellationToken);
                        }
                        await learningTimeTrackingService.TrackAsync(session, cancellationToken, sendClientLog);
                    }

                    var completedMessage = $"Đã hoàn thành bài lý thuyết: {plannedItem.name} (course={courseIid} item={current.Iid}).";
                    logger.LogInformation("{Message}", completedMessage);
                    await sendClientLog(completedMessage, cancellationToken);
                }
            }

            var refreshedPlan = await refreshStudyPlan();
            if (refreshedPlan.CompletionResult != AccountStudyResult.Pending)
            {
                return refreshedPlan.CompletionResult;
            }

            pending = GetTheoryItems(refreshedPlan.Items);
            if (pending.Count == 0 || savedThisPass == 0)
            {
                return AccountStudyResult.Pending;
            }
        }

        var finalPlan = await refreshStudyPlan();
        return finalPlan.CompletionResult;
    }

    private static List<CheckingTimeDTO.AutoStudy> GetTheoryItems(
        IEnumerable<CheckingTimeDTO.AutoStudy> items)
    {
        var pending = items
            .Where(item => item.typeAutoStudy == CheckingTimeDTO.TypeAutoStudy.AutoStudy)
            .DistinctBy(item => (item.iidRoot, item.iid, item.ntype))
            .ToList();

        foreach (var item in pending)
        {
            if (item.iid <= 0 || item.iidRoot is not > 0)
            {
                throw new InvalidDataException("Theory item must have positive item and course IDs.");
            }

            if (!IsTheoryType(item.ntype))
            {
                throw new InvalidDataException($"Unsupported theory item type '{item.ntype}'.");
            }
        }

        return pending;
    }

    private static IEnumerable<LearningTimeNode> Flatten(IEnumerable<LearningTimeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static bool IsTheoryType(string? itemNtype) =>
        string.Equals(itemNtype, "video", StringComparison.OrdinalIgnoreCase);

    private static void ValidateOptions(WorkerOptions options)
    {
        if (options.SaveItemSeconds is < 1 or > 15)
        {
            throw new InvalidDataException("Worker:SaveItemSeconds must be between 1 and 15.");
        }

        if (options.TrackTimeSeconds is < 1 or > 60)
        {
            throw new InvalidDataException("Worker:TrackTimeSeconds must be between 1 and 60.");
        }

        if (options.StopOnDailyKpi && options.KpiCheckEverySaves < 1)
        {
            throw new InvalidDataException("Worker:KpiCheckEverySaves must be positive when KPI checks are enabled.");
        }
    }
}
