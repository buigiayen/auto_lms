using System.Text.Json.Nodes;
using LotusLMS.Api;
using static LotusLMS.Worker.LotusLmsJson;

namespace LotusLMS.Worker;

/// <summary>Reads and submits one practice item, independently of video learning.</summary>
internal sealed class LotusLmsPractice(
    ILotusLmsApiClient apiClient,
    ILogger<LotusLmsPractice> logger,
    TimeProvider timeProvider)
{
    public async Task<PracticeResult> RunCourseAsync(
        LotusLmsSession session,
        int courseIid,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Course {CourseIid}: starting practice auto-complete.", courseIid);

        var detail = await apiClient.GetDetailLearningTimeAsync(
            session,
            courseIid,
            "practice",
            cancellationToken);
        var practiceIid = FindPracticeIid(detail);
        if (practiceIid <= 0)
        {
            return new PracticeResult(null, 0, 0, 0, 0, "no practice item");
        }

        return await RunPracticeAsync(session, practiceIid, courseIid, options, cancellationToken);
    }
    
    /// <summary>Recalculate, resolve metadata, read bank/sync, then create a fresh take.</summary>
    public async Task<IReadOnlyList<PracticeCourseReport>> RunTargetAsync(
        LotusLmsSession session,
        int planIds,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        var target = await apiClient.GetStudentCoursesAsync(session, planIds,
            cancellationToken: cancellationToken);
        var results = new List<PracticeCourseReport>();

        var targetQuery = LotusLmsAccountReader.Flatten(target).Where(p =>
            p?.Course is { IsPass: false } course
            && !string.Equals(p.Name ?? course.Name, "Mô phỏng các tình huống giao thông", StringComparison.OrdinalIgnoreCase)
            && course.__Expand?.ScoreByRubric?.score_by_rubrik?.FirstOrDefault(score =>
                string.Equals(score?.name, "Ôn luyện", StringComparison.OrdinalIgnoreCase))?.Passed == false).ToList();

        foreach (var item in targetQuery)
        {
            var course = item.Course!;
            var practiceIid = course.__Expand?.Syllabus?.children?.FirstOrDefault(c => c?.ntype == "practice")?.iid ?? 0;
            var result = await RunPracticeAsync(session, practiceIid, course.Iid, options, cancellationToken);
            results.Add(new PracticeCourseReport(course.Iid, course.Name ?? item.Name ?? "(unnamed course)", result));
        }

        return results;
    }

    private async Task<PracticeResult> RunPracticeAsync(
        LotusLmsSession session,
        int practiceIid,
        int courseIid,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        // The capture's time_start refers to entering practice, before start-practice.
        var timeStart = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        await apiClient.RecalculateCoursePracticeProgressAsync(session, practiceIid, courseIid, cancellationToken);
        var practiceDetail = await apiClient.GetPracticeDetailAsync(session, practiceIid, courseIid, cancellationToken);
        var sco = practiceDetail?.Result?._expand?.ExamSco;
        if (sco is null)
        {
            throw new InvalidDataException("Practice detail has no expanded exam_sco.");
        }

        var examScoIid = ReadPositiveInt(sco.Iid);
        var bankIid = ReadPositiveInt((sco.QuestionBanks)?[0]);

        if (examScoIid <= 0 || bankIid <= 0)
        {
            throw new InvalidDataException("Practice detail requires positive exam_sco.iid and question_banks[0].");
        }

        var bank = await apiClient.GetQuestionBankStatusAsync(
            session,
            // Despite the query field being named exam_template_iid, the web
            // passes exam_sco.iid. The nested exercise template returns no rows.
            examScoIid,
            bankIid,
            courseIid,
            cancellationToken);

        if (bank.Result?.Questions?.Result is not { Count: > 0 })
        {
            throw new InvalidDataException("Question bank has questions; cannot auto-answer a non-empty bank.");
        }


        // The sync result id identifies aggregate history, not a practice take.
        var started = await apiClient.StartPracticeAsync(session, practiceIid, examScoIid, courseIid, cancellationToken);
        var takeId = ReadString(started["result"]?["id"]);
        if (string.IsNullOrWhiteSpace(takeId))
            throw new InvalidDataException("start-practice did not return result.id; no practice results were saved.");

        return await SubmitQuestionBankAsync(session, practiceIid, examScoIid, courseIid,
           bank.Result, takeId, timeStart, new HashSet<string>(), options, cancellationToken);
    }

    private async Task<PracticeResult> SubmitQuestionBankAsync(
        LotusLmsSession session,
        int practiceIid,
        int examScoIid,
        int courseIid,
        GetQuestionBankStatus bank,
        string takeId,
        long timeStart,
        ISet<string> doneIids,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        var questions = bank.Questions?.Result;
        if (questions is null || questions.Count == 0)
        {
            return new PracticeResult(practiceIid, 0, 0, 0, 0, "question bank empty");
        }

        logger.LogInformation("Practice {PracticeIid}: {QuestionCount} questions in bank.", practiceIid, questions.Count);

        var firstQuestion = questions.FirstOrDefault(question => question is not null);
        var questionCategory = new[]
        {
            bank.QuestionCategory,
            firstQuestion?.QuestionCategory,
            firstQuestion?.Category,
            firstQuestion?.Tags?.FirstOrDefault()
        }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

        var pending = new List<ResultQuestions>();
        foreach (var question in questions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.Iid) || !QuestionIsCorrect(question))
            {
                continue;
            }

            pending.Add(question);
        }

        if (pending.Count == 0)
        {
            return new PracticeResult(practiceIid, questions.Count, 0, 0, 0,
                "question bank has no questions with a valid ID and is_answer=1");
        }

        logger.LogInformation(
            "Practice {PracticeIid}: {EligibleCount}/{QuestionCount} questions have a valid ID and is_answer=1.",
            practiceIid, pending.Count, questions.Count);

        var submitted = 0;
        var batches = 0;
        var total = pending.Count;
        // Prior correct answers do not imply KPI completion. The HAR repeats
        // questions that have correct > 0, including ones whose last answer was wrong.
        var alreadyCorrect = pending.Count(question => doneIids.Contains(question.Iid.Trim()));
        var gainedKpiSeconds = 0d;
        var alreadyOvertime = false;
        var serverFinished = false;
        var previousBatchTimestamp = timeProvider.GetTimestamp();
        var schedule = new PracticeBatchSchedule(
            TimeSpan.FromSeconds(options.PracticeIntervalSeconds), timeProvider,
            async (seconds, token) =>
            {
                await apiClient.TrackPracticeTimeAsync(session, practiceIid, courseIid, seconds, token);
            });

        for (var offset = 0; offset < total; offset += options.PracticeBatchSize)
        {
            var batch = new List<ResultQuestions>();
            for (var index = offset; index < Math.Min(offset + options.PracticeBatchSize, total); index++)
            {
                batch.Add(pending[index]);
            }

            var isLast = offset + options.PracticeBatchSize >= total;
            var intervalSeconds = Random.Shared.Next(options.PracticeIntervalSeconds, options.PracticeMaxIntervalSeconds + 1);
            logger.LogInformation("Practice {PracticeIid}: waiting for a {IntervalSeconds}s submission interval.",
                practiceIid, intervalSeconds);
            await schedule.WaitAsync(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
            var batchTimestamp = timeProvider.GetTimestamp();
            var elapsedMilliseconds = (long)timeProvider.GetElapsedTime(previousBatchTimestamp, batchTimestamp).TotalMilliseconds;
            previousBatchTimestamp = batchTimestamp;
            var saved = await SubmitPracticeBatchAsync(
                session,
                practiceIid,
                examScoIid,
                courseIid,
                batch,
                takeId,
                timeStart,
                questionCategory ?? string.Empty,
                isLast,
                options,
                elapsedMilliseconds,
                cancellationToken);
            submitted += saved.Submitted;
            if (saved.Submitted > 0) batches++;
            takeId = saved.TakeId;
            gainedKpiSeconds += saved.GainedKpiSeconds;
            alreadyOvertime |= saved.AlreadyOvertime;
            serverFinished = saved.ServerFinished;

            logger.LogInformation(
                "Practice {PracticeIid}: submitted {Submitted}/{Total} questions.",
                practiceIid,
                submitted,
                total);
            if (serverFinished) break;
        }

        await schedule.FlushTrackingAsync(cancellationToken);
        return new PracticeResult(practiceIid, questions.Count, alreadyCorrect, submitted, batches,
            $"submitted {submitted}/{total} questions; KPI gained={gainedKpiSeconds:0.###}s; "
            + $"already overtime={alreadyOvertime}; server finished={serverFinished}",
            gainedKpiSeconds, alreadyOvertime, serverFinished);
    }

    private async Task<(int Submitted, string TakeId, double GainedKpiSeconds, bool AlreadyOvertime, bool ServerFinished)> SubmitPracticeBatchAsync(
        LotusLmsSession session,
        int practiceIid,
        int examScoIid,
        int courseIid,
        IReadOnlyList<ResultQuestions> batch,
        string takeId,
        long timeStart,
        string questionCategory,
        bool isLast,
        WorkerOptions options,
        long elapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        var items = new List<PracticeTakeItem>();
        var correctCount = 0;
        var timeUsed = 0d;

        var questionIndex = 0;
        foreach (var question in batch)
        {
            var questionId = question.Iid?.Trim();
            if (string.IsNullOrWhiteSpace(questionId))
            {
                continue;
            }

            var isCorrect = QuestionIsCorrect(question);
            if (isCorrect)
            {
                correctCount++;
            }

            var spentMilliseconds = elapsedMilliseconds / batch.Count
                + (questionIndex < elapsedMilliseconds % batch.Count ? 1 : 0);
            questionIndex++;
            var spentSeconds = spentMilliseconds / 1000d;
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
                DoingMilliseconds = checked((int)spentMilliseconds),
                // This worker does not open an answer-key panel.
                ViewKeyMilliseconds = 0
            });
        }

        if (items.Count == 0)
        {
            return (0, takeId, 0, false, false);
        }

        var response = await apiClient.SavePracticeTakeAsync(
            session,
            new SavePracticeTakeRequest
            {
                PracticeIid = practiceIid,
                ExamScoIid = examScoIid,
                CourseIid = courseIid,
                TakeId = takeId,
                Finished = options.MarkPracticeFinished && isLast,
                TimeStartUnixSeconds = timeStart,
                TimeUsedSeconds = timeUsed,
                Score = correctCount,
                PaperScore = items.Count,
                QuestionCategory = questionCategory,
                SaveProgressForCourse = true,
                Items = items
            },
            cancellationToken: cancellationToken);
        var alreadyOvertime = ReadFlag(response["result"]?["already_overtime_before_this_api"]);
        var serverFinished = ReadFlag(response["result"]?["take_is_finished_automatically_by_server"]);
        if (alreadyOvertime)
            logger.LogWarning("Practice {PracticeIid}: server reports overtime before this save; a successful save does not imply KPI credit.", practiceIid);
        if (serverFinished)
            logger.LogWarning("Practice {PracticeIid}: server finished the take ({Reason}); stopping remaining batches.",
                practiceIid, ReadString(response["result"]?["reason_server_finish_practice_take_automatically"]));

        return (items.Count,
            ReadString(response["result"]?["take_id"])
                ?? ReadString(response["result"]?["id"])
                ?? takeId,
            ReadSeconds(response["result"]?["you_have_just_gained"]), alreadyOvertime, serverFinished);
    }

    private static bool ReadFlag(JsonNode? value) =>
        ReadString(value) is "1" or "true";

    private static double ReadSeconds(JsonNode? value) =>
        double.TryParse(ReadString(value), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var seconds)
        && double.IsFinite(seconds) && seconds > 0 ? seconds : 0;

    private static bool QuestionIsCorrect(ResultQuestions question) =>
        question.McAnswers?.Any(option => option?.IsAnswer == 1) == true
        || question.Answers2?.Any(option => option?.IsAnswer == 1) == true;

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
}
