using System.Reflection;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using LotusLMS.Worker.V2;
using LotusLMS.Worker.V2.Accounts;
using LotusLMS.Worker.V2.LotusRunner.CheckingTime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class StudyLoggingChecks
{
    public static async Task RunAsync(LotusLmsSession session, Action<bool, string> check)
    {
        var scopes = new LoggerExternalScopeProvider();
        var formatter = new GreenConsoleFormatter();
        var entry = new LogEntry<string>(LogLevel.Information, "fixture", new EventId(), "message", null, (text, _) => text);
        using var writer = new StringWriter();
        using (scopes.Push(new AccountLogScope("lane-A", "account-A", "run-A", 99)))
        using (scopes.Push(new Dictionary<string, object> { ["token"] = "fixture-secret" }))
            formatter.Write(in entry, scopes, writer);
        check(writer.ToString().Contains("[lane=lane-A account=account-A run=run-A plan=99]")
            && !writer.ToString().Contains("fixture-secret"),
            "console renders account ownership fields and ignores arbitrary scopes containing secrets");
        using var afterScope = new StringWriter();
        formatter.Write(in entry, scopes, afterScope);
        check(!afterScope.ToString().Contains("account-A"), "completed account scope does not leak into later logs");

        var api = DispatchProxy.Create<ILotusLmsApiClient, PracticeLoggingApi>();
        var fake = (PracticeLoggingApi)(object)api;
        var logger = new StudyTestLogger<PracticeServices>();
        var service = new PracticeServices(api, TimeProvider.System,
            new LearningTimeTrackingService(api, NullLogger<LearningTimeTrackingService>.Instance), logger,
            Options.Create(new WorkerOptions { PracticeBatchSize = 1, PracticeIntervalSeconds = 1, PracticeMaxIntervalSeconds = 1 }));
        var item = new CheckingTimeDTO.AutoStudy
            { iid = 22, iidRoot = 11, name = "Fixture practice", typeAutoStudy = CheckingTimeDTO.TypeAutoStudy.PracticeOnly };
        var refreshes = 0;
        var result = await service.RunAsync(session, [item],
            () => Task.FromResult(++refreshes == 1 ? new StudyPlan([item], false) : new StudyPlan([], true)),
            (_, _) => Task.CompletedTask, default);
        check(result == AccountStudyResult.Completed && fake.Saves.Count == 4,
            "logging preserves two practice attempts with two batches each");
        var repeat = logger.Entries.Single(entry => entry.Message.StartsWith("PRACTICE_REPEAT "));
        check(repeat.Level == LogLevel.Warning && (int)repeat.Fields["Attempt"]! == 2,
            "reselecting the same pending practice emits an explicit repeat warning");
        var sends = logger.Entries.Where(entry => entry.Message.StartsWith("PRACTICE_SEND ")).ToList();
        var acks = logger.Entries.Where(entry => entry.Message.StartsWith("PRACTICE_ACK ")).ToList();
        check(sends.Count == 4 && acks.Count == 4
            && sends.Select(entry => entry.Fields["SendId"]).Distinct().Count() == 4
            && sends.All(send => acks.Count(ack => Equals(send.Fields["SendId"], ack.Fields["SendId"])) == 1),
            "every practice batch has a unique send ID and one matching acknowledgement");
        check(sends.Select(entry => $"{entry.Fields["Attempt"]}/{entry.Fields["Batch"]}/{entry.Fields["TakeId"]}/{entry.Fields["QuestionIds"]}")
            .SequenceEqual(["1/1/take-1/question-1", "1/2/take-1/question-2", "2/1/take-2/question-1", "2/2/take-2/question-2"]),
            "practice logs identify exact take, attempt, batch and question IDs when questions repeat");
        check(logger.Entries.All(entry => !entry.Message.Contains(session.Token)
            && !entry.Message.Contains(session.SessionId) && !entry.Message.Contains("fixture-answer-text")),
            "study logs exclude authentication tokens, session IDs and answer text");

        fake.FailSave = true;
        logger.Entries.Clear();
        try
        {
            await service.RunAsync(session, [item], () => Task.FromResult(new StudyPlan([], true)),
                (_, _) => Task.CompletedTask, default);
            throw new Exception("FAIL: failed save fixture should fail");
        }
        catch (LotusLmsApiException)
        {
            check(logger.Entries.Count(entry => entry.Message.StartsWith("PRACTICE_SEND ")) == 1
                && !logger.Entries.Any(entry => entry.Message.StartsWith("PRACTICE_ACK ")),
                "a failed or unconfirmed save never produces a successful acknowledgement log");
        }
    }
}

internal sealed class StudyTestLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Dictionary<string, object?> Fields)> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? error, Func<TState, Exception?, string> format)
        => Entries.Add((level, format(state, error),
            state is IEnumerable<KeyValuePair<string, object?>> fields ? fields.ToDictionary() : []));
}

internal class PracticeLoggingApi : DispatchProxy
{
    private int _takes;
    public bool FailSave { get; set; }
    public List<SavePracticeTakeRequest> Saves { get; } = [];
    protected override object Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        nameof(ILotusLmsApiClient.GetPracticeDetailAsync) => Task.FromResult(new BaseApi<GetPracticeDetail>
            { Result = new GetPracticeDetail { _expand = new _expand { ExamSco = new ExamSco { Iid = 33, QuestionBanks = [44] } } } }),
        nameof(ILotusLmsApiClient.GetQuestionBankStatusAsync) => Task.FromResult(new BaseApi<GetQuestionBankStatus>
            { Result = new GetQuestionBankStatus { Questions = new Questions { Result = [Question("question-1"), Question("question-2")] } } }),
        nameof(ILotusLmsApiClient.StartPracticeAsync) => Task.FromResult(new JsonObject
            { ["result"] = new JsonObject { ["id"] = $"take-{++_takes}" } }),
        nameof(ILotusLmsApiClient.SavePracticeTakeAsync) => Save((SavePracticeTakeRequest)args![1]!),
        nameof(ILotusLmsApiClient.TrackPracticeTimeAsync) => Task.FromResult(new JsonObject()),
        nameof(ILotusLmsApiClient.GetTodayKpiTimeAsync) => Task.FromResult(new GetToDayKpiTime
            { TodayGainedKpiTime = new TodayGainedKpiTime { MaxOnlineLearningTimeInSeconds = 43200 } }),
        _ => throw new NotSupportedException(method?.Name)
    };
    private Task<JsonObject> Save(SavePracticeTakeRequest request)
    {
        if (FailSave) throw new LotusLmsApiException("Fixture unconfirmed save", "practice/api/save-take");
        Saves.Add(request);
        return Task.FromResult(new JsonObject { ["result"] = new JsonObject { ["take_id"] = request.TakeId } });
    }
    private static ResultQuestions Question(string id) => new()
        { Iid = id, McAnswers = [new McAnswers { IsAnswer = 1, Text = "fixture-answer-text" }] };
}
