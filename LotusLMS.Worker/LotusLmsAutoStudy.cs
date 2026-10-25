using System.Globalization;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using static LotusLMS.Worker.LotusLmsJson;

namespace LotusLMS.Worker;

/// <summary>
/// Mirrors the active flow of the WinForms client: login, enumerate the first
/// enrolment plan, then for each course walk the learning tree and submit
/// save-item-learning-time + track-time calls for each incomplete video in
/// course order, optionally followed by an auto-completed practice batch.
/// </summary>
internal sealed class LotusLmsAutoStudy(
    ILotusLmsApiClient apiClient,
    LotusLmsAccountReader accountReader,
    LotusLmsPractice practice,
    ILogger<LotusLmsAutoStudy> logger)
{
    public async Task<AutoStudyReport> RunAsync(
        LotusLmsCredentialsOptions credentials,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(options);

        var startedAt = DateTimeOffset.UtcNow;
        var session = await accountReader.LoginAsync(credentials, cancellationToken);

        logger.LogInformation(
            "Auto-study logged in as {LoginName} (user {UserId}).",
            session.LoginName,
            session.UserId);

        var planIds = await accountReader.ReadPlanIdsAsync(session, options, cancellationToken);

        var courseResults = new List<AutoStudyCourseResult>();
        var learningSaves = 0;
        var trackCalls = 0;
        var dailyKpiReached = false;
        var plansProcessed = 0;

        foreach (var planIid in planIds)
        {
            plansProcessed++;
            var courses = await accountReader.ReadCoursesAsync(session, planIid, options, includePassed: false, cancellationToken);

            foreach (var course in courses)
            {
                var result = await ProcessCourseAsync(session, planIid, course, options, cancellationToken);
                courseResults.Add(result);
                learningSaves += result.LearningTimeSaves;
                trackCalls += result.TrackTimeCalls;
                dailyKpiReached |= result.DailyKpiReached;

                if (result.DailyKpiReached && options.StopOnDailyKpi)
                {
                    logger.LogInformation(
                        "Daily KPI reached while processing course {CourseIid}; stopping auto-study.",
                        course.Iid);
                    break;
                }
            }

            if (dailyKpiReached && options.StopOnDailyKpi)
            {
                break;
            }
        }

        return new AutoStudyReport(
            startedAt,
            DateTimeOffset.UtcNow,
            plansProcessed,
            courseResults.Count,
            learningSaves,
            trackCalls,
            dailyKpiReached,
            courseResults);
    }

    private async Task<AutoStudyCourseResult> ProcessCourseAsync(
        LotusLmsSession session,
        int planIid,
        CourseDescriptor course,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        logger.LogInformation("Processing course {CourseIid}: {CourseName}", course.Iid, course.Name);

        var learning = await RunLearningLoopAsync(session, course, options, warnings, cancellationToken);

        if (!learning.DailyKpiReached)
        {
            logger.LogInformation(
                "Course {CourseIid}: all required video time is complete; advancing to the next course.",
                course.Iid);
        }

        string? practiceOutcome = null;
        if (options.IncludePractice && !(learning.DailyKpiReached && options.StopOnDailyKpi))
        {
            practiceOutcome = (await practice.RunCourseAsync(session, course.Iid, options, cancellationToken)).Outcome;
        }

        return new AutoStudyCourseResult(
            planIid,
            course.Iid,
            course.Name,
            learning.Saves,
            learning.TrackCalls,
            learning.ItemsCompleted,
            learning.DailyKpiReached,
            practiceOutcome,
            warnings);
    }

    private async Task<LearningLoopResult> RunLearningLoopAsync(
        LotusLmsSession session,
        CourseDescriptor course,
        WorkerOptions options,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var saves = 0;
        var tracks = 0;
        var itemsCompleted = 0;
        var kpiReached = false;

        var detail = await apiClient.GetDetailLearningTimeAsync(
            session,
            course.Iid,
            "video",
            cancellationToken);
        var items = FlattenLearningItems(detail).Where(p=>p.MinSeconds <= 0 || p.ActualSeconds < p.MinSeconds).ToList();
        var actualByIid = CollectActualLearningTimes(detail);

        foreach (var item in items)
        {
            if (kpiReached && options.StopOnDailyKpi)
            {
                break;
            }

            var current = item with
            {
                ActualSeconds = actualByIid.TryGetValue(item.Iid, out var actual)
                    ? actual
                    : item.ActualSeconds
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                if (current.MinSeconds <= 0 || current.ActualSeconds >= current.MinSeconds)
                {
                    if (current.MinSeconds > 0)
                    {
                        itemsCompleted++;
                    }

                    break;
                }

                var secondsToSave = (int)Math.Ceiling(Math.Min(
                    options.SaveItemSeconds,
                    current.MinSeconds - current.ActualSeconds));

                // A save represents elapsed study time. Wait first so the initial
                // request cannot claim time before the video has actually run.
                await Task.Delay(
                    TimeSpan.FromSeconds(options.SaveItemSeconds),
                    cancellationToken);

                if (!await TrySaveItemLearningTimeAsync(
                        session,
                        current,
                        course.Iid,
                        secondsToSave,
                        warnings,
                        cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"Sequential auto-study stopped because saving video {current.Iid} " +
                        $"in course {course.Iid} failed.");
                }

                saves++;

                try
                {
                    var fresh = await apiClient.GetDetailLearningTimeAsync(
                        session,
                        course.Iid,
                        "video",
                        cancellationToken);
                    var freshActual = CollectActualLearningTimes(fresh);
                    current = current with
                    {
                        ActualSeconds = freshActual.TryGetValue(current.Iid, out var serverSeconds)
                            ? serverSeconds
                            : current.ActualSeconds + secondsToSave
                    };
                }
                catch (Exception exception)
                {
                    warnings.Add($"re-fetch learning time for item {current.Iid} failed: {exception.Message}");
                    current = current with { ActualSeconds = current.ActualSeconds + secondsToSave };
                }

                logger.LogDebug(
                    "Saved {Seconds}s for item {ItemIid} ({ItemName}); {Remaining}s remaining.",
                    secondsToSave,
                    current.Iid,
                    current.Name,
                    Math.Max(0, current.MinSeconds - current.ActualSeconds));

                if (options.StopOnDailyKpi && saves % options.KpiCheckEverySaves == 0)
                {
                    var kpi = await TryCheckKpiAsync(session, course.Iid, warnings, cancellationToken);
                    if (kpi is { IsReached: true })
                    {
                        kpiReached = true;
                        logger.LogInformation(
                            "Daily KPI reached for course {CourseIid} (gained={Gained}s, required={Required}s).",
                            course.Iid,
                            kpi.GainedSeconds,
                            kpi.RequiredSeconds);
                        break;
                    }

                    if (await TryTrackTimeAsync(
                            session,
                            course.Iid,
                            options.TrackTimeSeconds,
                            warnings,
                            cancellationToken))
                    {
                        tracks++;
                    }
                }

            }
        }

        return new LearningLoopResult(saves, tracks, itemsCompleted, kpiReached);
    }

    private async Task<bool> TrySaveItemLearningTimeAsync(
        LotusLmsSession session,
        LearningItem item,
        int courseIid,
        int seconds,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            await apiClient.SaveItemLearningTimeAsync(
                session,
                new SaveItemLearningTimeRequest
                {
                    ItemIid = item.Iid,
                    ItemNtype = item.Ntype,
                    CourseIid = courseIid,
                    TimeSpentSeconds = seconds
                },
                cancellationToken);
            return true;
        }
        catch (LotusLmsApiException exception)
        {
            warnings.Add(
                $"save-item-learning-time for item {item.Iid} failed: {exception.Message}; " +
                $"server message: {exception.ServerMessage ?? "(none)"}");
            return false;
        }
        catch (Exception exception)
        {
            warnings.Add($"save-item-learning-time for item {item.Iid} failed: {exception.Message}");
            return false;
        }
    }

    private async Task<KpiSnapshot?> TryCheckKpiAsync(
        LotusLmsSession session,
        int courseIid,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.GetTodayKpiTimeAsync(session, courseIid, cancellationToken);
            var kpi = response?.TodayGainedKpiTime;
            return kpi is null ? new KpiSnapshot(0, 0, null)
                : new KpiSnapshot(kpi.TotalGainedKpiTimeInSeconds, kpi.MaxGainedKpiTimeInSeconds,
                    kpi.MaxGainedKpiTimeInSeconds > 0 ? kpi.TotalGainedKpiTimeInSeconds >= kpi.MaxGainedKpiTimeInSeconds : null);
        }
        catch (Exception exception)
        {
            warnings.Add($"KPI check failed: {exception.Message}");
            return null;
        }
    }

    private async Task<bool> TryTrackTimeAsync(
        LotusLmsSession session,
        int courseIid,
        double seconds,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            await apiClient.TrackTimeAsync(
                session,
                new TrackTimeRequest
                {
                    CourseIid = courseIid,
                    TimeSpentSeconds = seconds,
                    PageId = "learn"
                },
                cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            warnings.Add($"track-time failed: {exception.Message}");
            return false;
        }
    }

    private static List<LearningItem> FlattenLearningItems(
        BaseApi<ResultDetailLearningTime> response)
    {
        var items = new List<LearningItem>();
        if (response.Result is not null && response.Result.IsSuccess == false)
        {
            CollectLearningItems(response.Result.Children.Where(p => !p.IsSuccessLearningTime).ToList(), items);
        }

        return items;
    }

    private static void CollectLearningItems(
        IReadOnlyList<LearningTimeNode> children,
        List<LearningItem> output)
    {
        foreach (var item in children)
        {
            if (item.Children.Count > 0)
            {
                CollectLearningItems(item.Children, output);
            }

            // SCO nodes are aggregate/grouping parents. Their time is derived
            // from descendants, so only concrete videos receive saves.
            if (!string.Equals(
                    item.Ntype,
                    "video",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.Iid <= 0)
            {
                continue;
            }

            output.Add(new LearningItem(
                item.Iid,
                "video",
                item.Name ?? "(unnamed item)",
                item.MinLearningTime,
                item.ActualLearningTime));
        }
    }

    private static Dictionary<int, double> CollectActualLearningTimes(
        BaseApi<ResultDetailLearningTime> response)
    {
        var map = new Dictionary<int, double>();
        if (response.Result is not null)
        {
            if (response.Result.Iid > 0)
            {
                map[response.Result.Iid] = response.Result.ActualLearningTime;
            }

            foreach (var child in response.Result.Children)
            {
                CollectActualTimes(child, map);
            }
        }

        return map;
    }

    private static void CollectActualTimes(LearningTimeNode item, Dictionary<int, double> map)
    {
        if (item.Iid > 0)
        {
            map[item.Iid] = item.ActualLearningTime;
        }

        foreach (var child in item.Children)
        {
            CollectActualTimes(child, map);
        }
    }

    private static KpiSnapshot ParseKpi(JsonObject? result)
    {
        if (result is null)
        {
            return new KpiSnapshot(0, 0, null);
        }

        var kpi = result["today_gained_kpi_time"] as JsonObject;

        var gained = ReadFiniteDouble(kpi?["total_gained_kpi_time_in_seconds"])
            ?? ReadFiniteDouble(kpi?["gained_time"])
            ?? ReadFiniteDouble(result["gained_time"])
            ?? ReadFiniteDouble(result["today_gained_time"])
            ?? ReadFiniteDouble(result["total_gained_time"])
            ?? 0d;

        var required = ReadFiniteDouble(kpi?["max_gained_kpi_time_in_seconds"])
            ?? ReadFiniteDouble(kpi?["required_time"])
            ?? ReadFiniteDouble(result["required_time"])
            ?? ReadFiniteDouble(result["total_kpi_time"])
            ?? 0d;

        bool? reached = null;
        if (TryReadBool(result["is_reached_kpi"], out var reachedFlag))
        {
            reached = reachedFlag;
        }
        else if (kpi is not null && TryReadBool(kpi["max_online_learning_time_reached"], out reachedFlag))
        {
            reached = reachedFlag;
        }
        else if (required > 0)
        {
            reached = gained >= required;
        }

        return new KpiSnapshot(gained, required, reached);
    }

    private static double? ReadFiniteDouble(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return double.IsFinite(number) ? number : null;
        }

        return double.TryParse(
            ReadString(node),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number)
            && double.IsFinite(number)
                ? number
                : null;
    }

    private static bool TryReadBool(JsonNode? node, out bool result)
    {
        result = false;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out result))
            {
                return true;
            }

            if (value.TryGetValue<int>(out var number))
            {
                result = number != 0;
                return true;
            }
        }

        var text = ReadString(node);
        if (bool.TryParse(text, out result))
        {
            return true;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed != 0;
            return true;
        }

        return false;
    }

    private sealed record LearningItem(
        int Iid,
        string Ntype,
        string Name,
        int MinSeconds,
        double ActualSeconds);

    private sealed record KpiSnapshot(
        double GainedSeconds,
        double RequiredSeconds,
        bool? IsReached);

    private sealed record LearningLoopResult(
        int Saves,
        int TrackCalls,
        int ItemsCompleted,
        bool DailyKpiReached);
}
