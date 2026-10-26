using System.Text.Json.Nodes;
using LotusLMS.Api;
using LotusLMS.Worker.V2;
using LotusLMS.Worker.V2.LotusRunner.CheckingTime;
using LotusLMS.Worker2;
using Microsoft.Extensions.Options;

public class PracticeServices(
    ILotusLmsApiClient client,
    TimeProvider timeProvider,
     LearningTimeTrackingService learningTimeTrackingService,
      ILogger<PracticeServices> logger,
     IOptions<WorkerOptions> workerOptions)
{
    public async Task<AccountStudyResult> RunAsync(LotusLmsSession lotusLmsSession,
     IReadOnlyCollection<CheckingTimeDTO.AutoStudy> AutoStudy,
     Func<Task<StudyPlan>> Callback,
     Func<string, CancellationToken, Task> sendClientLog,
     CancellationToken cancellationToken)
    {
        var pendingPractices = AutoStudy
            .Where(p => p.typeAutoStudy == CheckingTimeDTO.TypeAutoStudy.PracticeOnly)
            .ToList();
        var attempts = new Dictionary<(int? Course, int Practice), int>();

        while (pendingPractices.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = pendingPractices[0];
            var key = (item.iidRoot, item.iid);
            var attempt = attempts.GetValueOrDefault(key) + 1;
            attempts[key] = attempt;
            logger.LogInformation(
                "PRACTICE_SELECT course={CourseIid} practice={PracticeIid} attempt={Attempt} pending={Pending}",
                item.iidRoot, item.iid, attempt, pendingPractices.Count);
            if (attempt > 1)
                logger.LogWarning(
                    "PRACTICE_REPEAT course={CourseIid} practice={PracticeIid} attempt={Attempt} reason=still-pending-after-refresh",
                    item.iidRoot, item.iid, attempt);

            var timeStart = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            var getPracticeDetail = await client.GetPracticeDetailAsync(lotusLmsSession, item.iid, item.iidRoot!.Value, cancellationToken);
            var sco = getPracticeDetail?.Result?._expand?.ExamSco;
            if (sco is null)
            {
                throw new InvalidDataException("Practice detail has no expanded exam_sco.");
            }
            var examScoIid = sco.Iid;
            var bankIid = (sco.QuestionBanks)?[0];

            if (examScoIid <= 0 || bankIid <= 0)
            {
                throw new InvalidDataException("Practice detail requires positive exam_sco.iid and question_banks[0].");
            }

            var bank = await client.GetQuestionBankStatusAsync(
                lotusLmsSession,
                // Despite the query field being named exam_template_iid, the web
                // passes exam_sco.iid. The nested exercise template returns no rows.
                examScoIid,
                bankIid!.Value,
                item.iidRoot.Value,
                cancellationToken);

            if (bank.Result?.Questions?.Result is not { Count: > 0 })
            {
                throw new InvalidDataException("Question bank has questions; cannot auto-answer a non-empty bank.");
            }
            var startId = Guid.NewGuid().ToString("N");
            logger.LogInformation(
                "PRACTICE_START_SEND sendId={SendId} course={CourseIid} practice={PracticeIid} attempt={Attempt}",
                startId, item.iidRoot, item.iid, attempt);
            var started = await client.StartPracticeAsync(
                lotusLmsSession,
                item.iid,
                examScoIid,
                item.iidRoot.Value, cancellationToken);
            var takeId = LotusLMS.Worker2.LotusLmsJson.ReadString(started["result"]?["id"]);
            logger.LogInformation(
                "PRACTICE_START_ACK sendId={SendId} course={CourseIid} practice={PracticeIid} attempt={Attempt} take={TakeId}",
                startId, item.iidRoot, item.iid, attempt, takeId);

            await SubmitQuestionBankAsync(lotusLmsSession, item.iid, examScoIid, item.iidRoot.Value,
            bank.Result, takeId, timeStart, new HashSet<string>(), workerOptions.Value, attempt, cancellationToken);
            if (await learningTimeTrackingService.TrackAsync(
                    lotusLmsSession,
                    cancellationToken,
                    sendClientLog))
            {
                return AccountStudyResult.DailyKpiReached;
            }

            // Refresh the pending practice list instead of recursively starting
            // another RunAsync call while this invocation is still active.
            var refreshedPlan = await Callback();
            if (refreshedPlan.IsCompleted)
            {
                return AccountStudyResult.Completed;
            }

            pendingPractices = refreshedPlan.Items
                .Where(p => p.typeAutoStudy == CheckingTimeDTO.TypeAutoStudy.PracticeOnly)
                .ToList();
        }

        return AccountStudyResult.Pending;
    }



    private async Task SubmitQuestionBankAsync(
        LotusLmsSession session,
        int practiceIid,
        int examScoIid,
        int courseIid,
        GetQuestionBankStatus bank,
        string takeId,
        long timeStart,
        ISet<string> doneIids,
        WorkerOptions options,
        int attempt,
        CancellationToken cancellationToken)
    {
        var questions = bank.Questions?.Result;
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
                await client.TrackPracticeTimeAsync(session, practiceIid, courseIid, seconds, token);
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
                attempt,
                offset / options.PracticeBatchSize + 1,
                (total + options.PracticeBatchSize - 1) / options.PracticeBatchSize,
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
     int attempt,
     int batchNumber,
     int batchCount,
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

        var sendId = Guid.NewGuid().ToString("N");
        logger.LogInformation(
            "PRACTICE_SEND sendId={SendId} course={CourseIid} practice={PracticeIid} take={TakeId} attempt={Attempt} batch={Batch}/{BatchCount} questions={QuestionIds} seconds={Seconds}",
            sendId, courseIid, practiceIid, takeId, attempt, batchNumber, batchCount,
            string.Join(",", items.Select(item => item.QuestionId)), timeUsed);
        var response = await client.SavePracticeTakeAsync(
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
        var returnedTakeId = LotusLmsJson.ReadString(response["result"]?["take_id"])
            ?? LotusLmsJson.ReadString(response["result"]?["id"])
            ?? takeId;
        logger.LogInformation(
            "PRACTICE_ACK sendId={SendId} course={CourseIid} practice={PracticeIid} take={TakeId} returnedTake={ReturnedTakeId} attempt={Attempt} batch={Batch}/{BatchCount} gainedKpi={GainedKpi} overtime={Overtime} serverFinished={ServerFinished}",
            sendId, courseIid, practiceIid, takeId, returnedTakeId, attempt, batchNumber, batchCount,
            ReadSeconds(response["result"]?["you_have_just_gained"]), alreadyOvertime, serverFinished);
        if (alreadyOvertime)
            logger.LogWarning("Practice {PracticeIid}: server reports overtime before this save; a successful save does not imply KPI credit.", practiceIid);
        if (serverFinished)
            logger.LogWarning("Practice {PracticeIid}: server finished the take ({Reason}); stopping remaining batches.",
                practiceIid, LotusLmsJson.ReadString(response["result"]?["reason_server_finish_practice_take_automatically"]));

        return (items.Count,
            returnedTakeId,
            ReadSeconds(response["result"]?["you_have_just_gained"]), alreadyOvertime, serverFinished);
    }

    private static bool ReadFlag(JsonNode? value) =>
        LotusLmsJson.ReadString(value) is "1" or "true";
    private static double ReadSeconds(JsonNode? value) =>
    double.TryParse(LotusLmsJson.ReadString(value), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var seconds)
    && double.IsFinite(seconds) && seconds > 0 ? seconds : 0;


    private static bool QuestionIsCorrect(ResultQuestions question) =>
        question.McAnswers?.Any(option => option?.IsAnswer == 1) == true
        || question.Answers2?.Any(option => option?.IsAnswer == 1) == true;

}
