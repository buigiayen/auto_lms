using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using Microsoft.Extensions.Options;

namespace LotusLMS.Worker;

/// <summary>
/// Mirrors the active flow of the WinForms client: login, enumerate the first
/// enrolment plan, then for each course walk the learning tree and submit
/// save-item-learning-time + track-time calls for each incomplete video in
/// course order, optionally followed by an auto-completed practice batch.
/// </summary>
internal sealed class LotusLmsAutoStudy(
    ILotusLmsApiClient apiClient,
    IOptions<LotusLmsOptions> apiOptions,
    ILogger<LotusLmsAutoStudy> logger)
{
    public async Task<AutoStudyReport> RunAsync(
        LotusLmsCredentialsOptions credentials,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(options);

        EnsureCredentials(credentials);
        EnsureHttps(apiOptions.Value.BaseUrl, $"{LotusLmsOptions.SectionName}:BaseUrl");
        EnsureHttps(apiOptions.Value.WebOrigin, $"{LotusLmsOptions.SectionName}:WebOrigin");

        var startedAt = DateTimeOffset.UtcNow;

        var session = await apiClient.LoginAsync(
            new LotusLmsLoginRequest
            {
                Username = credentials.Username,
                Password = credentials.Password,
                DeviceUuid = credentials.DeviceUuid,
                SessionId = credentials.SessionId,
                RequestId = credentials.RequestId,
                RequestToken = credentials.RequestToken,
                StudentRequestId = credentials.StudentRequestId,
                StudentRequestToken = credentials.StudentRequestToken,
                ClientSyncToken = credentials.ClientSyncToken
            },
            cancellationToken);

        logger.LogInformation(
            "Auto-study logged in as {LoginName} (user {UserId}).",
            session.LoginName,
            session.UserId);

        var planIds = await ReadPlanIdsAsync(session, options, cancellationToken);

        var courseResults = new List<AutoStudyCourseResult>();
        var learningSaves = 0;
        var trackCalls = 0;
        var dailyKpiReached = false;
        var plansProcessed = 0;

        foreach (var planIid in planIds)
        {
            plansProcessed++;
            var courses = await ReadCoursesAsync(session, planIid, options, cancellationToken);

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

    private async Task<List<int>> ReadPlanIdsAsync(
        LotusLmsSession session,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        var planIds = new List<int>();
        var seen = new HashSet<int>();
        var pageLimit = options.OnlyFirstPlan ? 1 : options.MaxPages;

        for (var page = 1; page <= pageLimit; page++)
        {
            var response = await apiClient.GetStudentEnrolmentPlansAsync(
                session,
                page,
                options.PlanPageSize,
                cancellationToken);
            var rows = RequireResultArray(response, "enrolment plans");

            foreach (var row in rows)
            {
                if (row is not JsonObject obj)
                {
                    continue;
                }

                var iid = ReadPositiveInt(obj["iid"]);
                if (iid > 0 && seen.Add(iid))
                {
                    planIds.Add(iid);
                }
            }

            if (options.OnlyFirstPlan || rows.Count < options.PlanPageSize)
            {
                break;
            }

            if (page == options.MaxPages)
            {
                logger.LogWarning("Enrolment plan paging reached the configured page limit.");
            }
        }

        return planIds;
    }

    private async Task<List<CourseDescriptor>> ReadCoursesAsync(
        LotusLmsSession session,
        int planIid,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        var courses = new List<CourseDescriptor>();
        var seen = new HashSet<int>();
        var pageLimit = options.OnlyFirstCoursePage ? 1 : options.MaxPages;

        for (var page = 1; page <= pageLimit; page++)
        {
            var response = await apiClient.GetStudentCoursesAsync(
                session,
                planIid,
                page,
                options.CoursePageSize,
                cancellationToken);
            var rows = Flatten(response)
                .Where(p => p.Course is { IsPass: false })
                .ToList();

            foreach (var row in rows)
            {
                AddCourse(row, courses, seen);
            }

            if (options.OnlyFirstCoursePage || response.Count < options.CoursePageSize)
            {
                break;
            }

            if (page == options.MaxPages)
            {
                logger.LogWarning(
                    "Course paging for plan {PlanIid} reached the configured page limit.",
                    planIid);
            }
        }

        return courses;
    }

    private static IEnumerable<Student_course_in_enrolment_plan_view> Flatten(
        IEnumerable<Student_course_in_enrolment_plan_view> nodes)
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
            practiceOutcome = await TryAutoPracticeAsync(session, course, options, warnings, cancellationToken);
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
        var items = FlattenLearningItems(detail);
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

                var secondsToSave = Math.Min(
                    options.SaveItemSeconds,
                    current.MinSeconds - current.ActualSeconds);

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
            return ParseKpi(response["result"] as JsonObject);
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

    private async Task<string?> TryAutoPracticeAsync(
        LotusLmsSession session,
        CourseDescriptor course,
        WorkerOptions options,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Course {CourseIid}: starting practice auto-complete.", course.Iid);

            var detail = await apiClient.GetDetailLearningTimeAsync(
                session,
                course.Iid,
                "video",
                cancellationToken);
            var practiceIid = FindPracticeIid(detail);
            if (practiceIid <= 0)
            {
                return "no practice item";
            }

            logger.LogInformation("Course {CourseIid}: practice item {PracticeIid} found.", course.Iid, practiceIid);

            var practiceDetail = await apiClient.GetPracticeDetailAsync(
                session,
                practiceIid,
                course.Iid,
                cancellationToken);
            var sco = practiceDetail?["result"]?["_expand"]?["exam_sco"] as JsonObject;
            if (sco is null)
            {
                return "practice detail has no exam_sco";
            }

            var examScoIid = ReadPositiveInt(sco["iid"]);
            var examTemplateIid = ReadExamTemplateIid(sco);
            var bankIid = ReadPositiveInt((sco["question_banks"] as JsonArray)?[0]);

            if (examScoIid <= 0 || examTemplateIid <= 0 || bankIid <= 0)
            {
                return "practice metadata incomplete";
            }

            // The web client sends the per-exam request context extracted from the
            // practice-detail response instead of the fixed student context.
            var requestId = ReadString(sco["ri"]) ?? FindFirstString(practiceDetail, "ri") ?? string.Empty;
            var requestToken = ReadString(sco["rit"]) ?? FindFirstString(practiceDetail, "rit") ?? string.Empty;

            var bank = await apiClient.GetQuestionBankStatusAsync(
                session,
                examTemplateIid,
                bankIid,
                course.Iid,
                cancellationToken);
            var questions = bank?["result"]?["questions"]?["result"] as JsonArray;
            if (questions is null || questions.Count == 0)
            {
                return "question bank empty";
            }

            logger.LogInformation("Practice {PracticeIid}: {QuestionCount} questions in bank.", practiceIid, questions.Count);

            var questionCategory = ReadString(bank?["result"]?["questions"]?["question_category"])
                ?? ReadString(bank?["result"]?["question_category"]);

            if (string.IsNullOrEmpty(questionCategory) && questions[0] is JsonObject firstQuestion)
            {
                questionCategory = ReadString(firstQuestion["question_category"])
                    ?? ReadString(firstQuestion["category"]);
            }

            var takeId = string.Empty;
            var doneIids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var progress = await apiClient.GetSyncPracticeProgressAsync(
                    session,
                    practiceIid,
                    examScoIid,
                    course.Iid,
                    requestId,
                    requestToken,
                    cancellationToken);
                takeId = ReadString(progress?["result"]?["take_id"])
                    ?? ReadString(progress?["result"]?["id"])
                    ?? string.Empty;
                if (string.IsNullOrEmpty(requestId)) requestId = FindFirstString(progress, "ri") ?? string.Empty;
                if (string.IsNullOrEmpty(requestToken)) requestToken = FindFirstString(progress, "rit") ?? string.Empty;
                doneIids = ReadCorrectQuestionIids(progress);
            }
            catch (Exception exception)
            {
                warnings.Add($"sync practice progress failed: {exception.Message}");
            }

            var pending = new List<JsonNode?>();
            foreach (var question in questions)
            {
                if (question is not JsonObject questionObj)
                {
                    continue;
                }

                var questionId = ReadString(questionObj["iid"]);
                if (!string.IsNullOrWhiteSpace(questionId) && doneIids.Contains(questionId))
                {
                    continue;
                }

                pending.Add(question);
            }

            if (pending.Count == 0)
            {
                return $"all {questions.Count} questions already answered";
            }

            var random = new Random();
            var submitted = 0;
            var total = pending.Count;

            for (var offset = 0; offset < total; offset += options.PracticeBatchSize)
            {
                var batch = new List<JsonNode?>();
                for (var index = offset; index < Math.Min(offset + options.PracticeBatchSize, total); index++)
                {
                    batch.Add(pending[index]);
                }

                var isLast = offset + options.PracticeBatchSize >= total;
                submitted += await SubmitPracticeBatchAsync(
                    session,
                    practiceIid,
                    examScoIid,
                    course.Iid,
                    batch,
                    takeId,
                    questionCategory ?? string.Empty,
                    isLast,
                    requestId,
                    requestToken,
                    options,
                    warnings,
                    random,
                    cancellationToken);

                logger.LogInformation(
                    "Practice {PracticeIid}: submitted {Submitted}/{Total} questions.",
                    practiceIid,
                    submitted,
                    total);
            }

            return $"submitted {submitted}/{total} questions ({questions.Count} total, {doneIids.Count} already answered)";
        }
        catch (Exception exception)
        {
            warnings.Add($"auto practice failed: {exception.Message}");
            return "practice failed";
        }
    }

    private async Task<int> SubmitPracticeBatchAsync(
        LotusLmsSession session,
        int practiceIid,
        int examScoIid,
        int courseIid,
        IReadOnlyList<JsonNode?> batch,
        string takeId,
        string questionCategory,
        bool isLast,
        string requestId,
        string requestToken,
        WorkerOptions options,
        List<string> warnings,
        Random random,
        CancellationToken cancellationToken)
    {
        var items = new List<PracticeTakeItem>();
        var correctCount = 0;
        var timeUsed = 0d;

        foreach (var questionNode in batch)
        {
            if (questionNode is not JsonObject question)
            {
                continue;
            }

            var questionId = ReadString(question["iid"]);
            if (string.IsNullOrWhiteSpace(questionId))
            {
                continue;
            }

            var isCorrect = QuestionIsCorrect(question);
            if (isCorrect)
            {
                correctCount++;
            }

            var spentSeconds = Math.Round(3.0 + random.NextDouble() * 3.0, 3);
            timeUsed += spentSeconds;

            items.Add(new PracticeTakeItem
            {
                QuestionId = questionId,
                Touched = true,
                Correct = isCorrect,
                SpentSeconds = spentSeconds,
                Score = isCorrect ? 1m : 0m,
                Percent = isCorrect ? 1m : 0m,
                Weight = 1,
                DoingMilliseconds = (int)(spentSeconds * 1000 * 0.7),
                ViewKeyMilliseconds = (int)(spentSeconds * 1000 * 0.3)
            });
        }

        if (items.Count == 0)
        {
            return 0;
        }

        try
        {
            await apiClient.SavePracticeTakeAsync(
                session,
                new SavePracticeTakeRequest
                {
                    PracticeIid = practiceIid,
                    ExamScoIid = examScoIid,
                    CourseIid = courseIid,
                    TakeId = takeId,
                    Finished = options.MarkPracticeFinished && isLast,
                    TimeStartUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    TimeUsedSeconds = timeUsed,
                    Score = correctCount,
                    PaperScore = items.Count,
                    QuestionCategory = questionCategory,
                    SaveProgressForCourse = true,
                    Items = items
                },
                requestId,
                requestToken,
                cancellationToken);
            return items.Count;
        }
        catch (Exception exception)
        {
            warnings.Add($"save-take failed: {exception.Message}");
            return 0;
        }
    }

    private static int ReadExamTemplateIid(JsonObject sco)
    {
        var templateDetails = sco["template_details"] as JsonArray;
        var exerciseTemplates = templateDetails?[0] is JsonObject template
            ? template["exercise_template"] as JsonArray
            : null;
        return exerciseTemplates?[0] is JsonObject exerciseTemplate
            ? ReadPositiveInt(exerciseTemplate["exam_template_iid"])
            : 0;
    }

    private static bool QuestionIsCorrect(JsonObject question)
    {
        var options = question["mc_answers"] as JsonArray
            ?? question["answers2"] as JsonArray;
        if (options is null || options.Count == 0)
        {
            return true;
        }

        foreach (var option in options)
        {
            if (option is JsonObject optionObj && ReadString(optionObj["is_answer"]) == "1")
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindFirstString(JsonNode? node, string key)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue(key, out var value) && value is JsonValue valueNode)
            {
                var text = ReadString(valueNode);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            foreach (var property in obj)
            {
                var found = FindFirstString(property.Value, key);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var found = FindFirstString(item, key);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static HashSet<string> ReadCorrectQuestionIids(JsonObject? response)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (response?["result"]?["sync_data"] is not JsonArray syncData)
        {
            return result;
        }

        foreach (var item in syncData)
        {
            if (item is not JsonObject entry)
            {
                continue;
            }

            var iid = ReadString(entry["iid"]);
            if (string.IsNullOrWhiteSpace(iid))
            {
                continue;
            }

            if (ReadInt(entry["correct"]) > 0)
            {
                result.Add(iid);
            }
        }

        return result;
    }

    private static List<LearningItem> FlattenLearningItems(
        BaseApi<ResultDetailLearningTime> response)
    {
        var items = new List<LearningItem>();
        if (response.Result is not null)
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

    private static Dictionary<int, int> CollectActualLearningTimes(
        BaseApi<ResultDetailLearningTime> response)
    {
        var map = new Dictionary<int, int>();
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

    private static void CollectActualTimes(LearningTimeNode item, Dictionary<int, int> map)
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

    private static int FindPracticeIid(BaseApi<ResultDetailLearningTime> response)
    {
        if (response.Result is null)
        {
            return 0;
        }

        foreach (var child in response.Result.Children)
        {
            var found = FindPracticeIidRecursive(child);
            if (found > 0)
            {
                return found;
            }
        }

        return 0;
    }

    private static int FindPracticeIidRecursive(LearningTimeNode item)
    {
        if (string.Equals(item.Ntype, "practice", StringComparison.OrdinalIgnoreCase)
            && item.Iid > 0)
        {
            return item.Iid;
        }

        foreach (var child in item.Children)
        {
            var found = FindPracticeIidRecursive(child);
            if (found > 0)
            {
                return found;
            }
        }

        return 0;
    }

    private static void AddCourse(
        Student_course_in_enrolment_plan_view row,
        List<CourseDescriptor> courses,
        ISet<int> seen)
    {
        var course = row.Course;
        if (course is null)
        {
            return;
        }

        var iid = course.Iid;
        if (iid <= 0 || !seen.Add(iid))
        {
            return;
        }

        var name = NormalizeCourseName(course.Name)
            ?? NormalizeCourseName(row.Name)
            ?? "(unnamed course)";

        courses.Add(new CourseDescriptor(iid, name, course.__Expand?.ScoreByRubric?.Rubric.PassScore ?? 0, course.__Expand?.ScoreByRubric?.Rubric.MaxScore ?? 0, course.IsPass));
    }

    private static string? NormalizeCourseName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private static JsonArray RequireResultArray(JsonObject response, string operation) =>
        response["result"] as JsonArray
        ?? throw new InvalidDataException(
            $"The {operation} response is missing a result array.");

    private static void EnsureCredentials(LotusLmsCredentialsOptions credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.Username)
            || string.IsNullOrEmpty(credentials.Password))
        {
            throw new InvalidOperationException(
                "Auto-study credentials are not configured in the local secret provider.");
        }
    }

    private static void EnsureHttps(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Auto-study refuses to send credentials because {settingName} is not HTTPS.");
        }
    }

    private static int ReadInt(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return int.TryParse(
            ReadString(node),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out number)
            ? number
            : 0;
    }

    private static int ReadPositiveInt(JsonNode? node)
    {
        var value = ReadInt(node);
        return value > 0 ? value : 0;
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

    private static string? ReadString(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        var scalar = value.ToString();
        return string.IsNullOrWhiteSpace(scalar) ? null : scalar.Trim();
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
        int ActualSeconds);

    private sealed record KpiSnapshot(
        double GainedSeconds,
        double RequiredSeconds,
        bool? IsReached);

    private sealed record LearningLoopResult(
        int Saves,
        int TrackCalls,
        int ItemsCompleted,
        bool DailyKpiReached);

    private sealed record CourseDescriptor(int Iid, string Name, double Score = 0, double MaxScore = 0, bool IsPass = false);
}
