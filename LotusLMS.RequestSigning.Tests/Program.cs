using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using LotusLMS.RequestSigning;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using LotusLMS.Worker;

// Package-free compatibility suite. It never contacts a live LMS or submits work.
if (args.Contains("--worker-client-only", StringComparer.Ordinal))
{
    var clientChecks = await ClientWorkerChecks.RunAsync();
    Console.WriteLine($"PASS: {clientChecks} worker/client integration checks; no network requests.");
    return;
}
if (args.Contains("--device-identity-only", StringComparer.Ordinal))
{
    var deviceChecks = await DeviceIdentityChecks.RunAsync();
    Console.WriteLine($"PASS: {deviceChecks} automatic device identity checks; no network requests.");
    return;
}

var checks = await DeviceIdentityChecks.RunAsync();
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    checks++;
}

var vectors = JsonSerializer.Deserialize<List<CryptoJsVector>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/cryptojs-vectors.json")),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var syncVectors = JsonSerializer.Deserialize<List<ClientSyncVector>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/client-sync-vectors.json")),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

foreach (var cultureName in new[] { "en-US", "vi-VN", "ar-SA" })
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
    foreach (var vector in vectors)
    {
        var tokens = SandRequestTokenGenerator.Generate(
            vector.UserIid, Guid.Parse(vector.Uuid), vector.TimestampMilliseconds,
            Convert.FromHexString(vector.IvHex));
        Check(tokens.RequestId == vector.RequestId, "request id matches shipped JavaScript");
        Check(tokens.RequestToken == vector.RequestToken, "AES bytes match shipped CryptoJS");
    }
    foreach (var vector in syncVectors)
    {
        Check(SandClientSyncTokenGenerator.Generate(vector.AuthenticationToken, vector.TimestampUnixSeconds)
            == vector.ClientSyncToken, "client sync matches shipped JavaScript");
    }
}
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var pairs = Enumerable.Range(0, 128).AsParallel()
    .Select(_ => SandRequestTokenGenerator.Generate(456789)).ToArray();
Check(pairs.Select(pair => pair.RequestId).Distinct().Count() == pairs.Length, "fresh IDs under concurrency");
Check(pairs.Select(pair => pair.RequestToken.Split(':')[0]).Distinct().Count() == pairs.Length, "fresh IVs under concurrency");
foreach (var pair in pairs)
{
    Check(Decrypt(pair.RequestToken, 456789) == pair.RequestId, "generated pair decrypts correctly");
    Check(pair.RequestId.Length == 32 && pair.RequestId.All(c => char.IsAsciiHexDigitLower(c)), "lowercase MD5 hex");
}

List<KeyValuePair<string, string>> source =
[
    new("bank_iids[]", "1"), new("bank_iids[]", "2"),
    new("_sand_ri", "old-id"), new("_sand_ri", "duplicate-id"), new("_sand_rit", "old-token")
];
var signed = SandRequestTokenGenerator.Sign(source, 1);
Check(source.Count == 5 && source[2].Value == "old-id", "signing does not mutate caller data");
Check(signed.Count(field => field.Key == "bank_iids[]") == 2, "repeated business keys survive signing");
Check(signed.Count(field => field.Key == "_sand_ri") == 1 && signed.Count(field => field.Key == "_sand_rit") == 1,
    "captured/duplicate tokens are replaced by exactly one pair");
Check(Decrypt(signed.Single(field => field.Key == "_sand_rit").Value, 1)
    == signed.Single(field => field.Key == "_sand_ri").Value, "signed fields are a matching pair");

foreach (var iid in new[] { 0, -1 })
{
    try { SandRequestTokenGenerator.Generate(iid); throw new Exception("Accepted invalid iid"); }
    catch (ArgumentOutOfRangeException) { checks++; }
}

using var handler = new CaptureHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://lms.invalid/") };
var options = new LotusLmsOptions { LogApiResponses = false };
var api = new LotusLmsApiClient(http, Options.Create(options));
var session = await api.LoginAsync(new LotusLmsLoginRequest
{
    Username = "fixture-user", Password = "fixture-password",
    RequestId = "stale-login-id", RequestToken = "stale-login-token",
    StudentRequestId = "stale-student-id", StudentRequestToken = "stale-student-token",
    ClientSyncToken = "stale-client-sync"
});
CheckPair(handler.Requests[^1], null);
await api.GetStudentEnrolmentPlansAsync(session);
CheckPair(handler.Requests[^1], session.UserIid);
var practiceTree = await api.GetDetailLearningTimeAsync(session, 22, "practice");
CheckPair(handler.Requests[^1], session.UserIid);
Check(handler.Requests[^1].Fields["item_ntype"] == "practice"
    && practiceTree.Result?.Children.Single().Ntype == "practice", "practice-filtered tree is available to the worker");
Check(practiceTree.Result!.Children.Single().ActualLearningTime == 3.5,
    "fractional learning-time strings deserialize without integer conversion errors");
Check(practiceTree.Result.ActualLearningTime == 1.25,
    "fractional leaf times are summed without counting aggregate parents twice");
await api.GetPracticeDetailAsync(session, 11, 22);
CheckPair(handler.Requests[^1], session.UserIid);
await api.GetSyncPracticeProgressAsync(session, 11, 33, 22, "stale-exam-ri", "stale-exam-rit");
CheckPair(handler.Requests[^1], session.UserIid);
await api.GetQuestionBankStatusAsync(session, 44, 55, 22);
Check(handler.Requests[^1].Method == HttpMethod.Post, "question bank uses POST as supplied curl");
Check(handler.Requests[^1].Fields["lang"] == "vn", "question bank uses Vietnamese language");
CheckPair(handler.Requests[^1], session.UserIid);
Check(handler.Requests[^1].Fields["_sand_token"] == session.Token
    && handler.Requests[^1].Fields["_sand_uiid"] == session.UserIid.ToString(CultureInfo.InvariantCulture),
    "question bank includes authenticated identity with sync token");
await api.SavePracticeTakeAsync(session, new SavePracticeTakeRequest
{
    PracticeIid = 11, ExamScoIid = 33, CourseIid = 22,
    Finished = false, TimeStartUnixSeconds = 1_792_200_000, TimeUsedSeconds = 1,
    Score = 0, PaperScore = 1,
    Items = [new PracticeTakeItem
    {
        QuestionId = "fixture-question", Correct = false, SpentSeconds = 1,
        Score = 0, Percent = 0, DoingMilliseconds = 1000, ViewKeyMilliseconds = 0
    }]
}, "stale-exam-ri", "stale-exam-rit");
CheckPair(handler.Requests[^1], session.UserIid);
Check(handler.Requests[^1].Form["take_info[0][iid]"] == "fixture-question", "practice payload survives signing");
Check(handler.Requests[^1].Form["finished"] == "0", "practice flags survive signing");
Check(handler.Requests[^1].MediaType == "multipart/form-data", "save-take matches curl multipart encoding");
Check(handler.Requests[^1].Form["lang"] == "vn", "save-take sends Vietnamese language flag");
await api.GetPracticeDetailAsync(session with { UserIid = 1, Token = "other-fixture-token", UserId = "other-fixture-id" }, 11, 22);
CheckPair(handler.Requests[^1], 1, "other-fixture-token");
Check(handler.Requests.Select(request => request.Fields["_sand_ri"]).Distinct().Count() == handler.Requests.Count,
    "all endpoints sign independently, including separate accounts");

options.GenerateRequestTokens = false;
await api.GetSyncPracticeProgressAsync(session, 11, 33, 22, "captured-id", "captured-token");
Check(handler.Requests[^1].Form["_sand_ri"] == "captured-id"
    && handler.Requests[^1].Form["_sand_rit"] == "captured-token", "explicit legacy replay remains available");
Check(handler.Requests[^1].Form["_sand_client_sync_token"] == "stale-client-sync", "legacy replay preserves captured sync token");

var clock = new AdvancingTimeProvider();
using var practiceHandler = new CaptureHandler { PracticeScenario = true, PracticeOnlyScenario = true, ScheduleClock = clock };
using var practiceHttp = new HttpClient(practiceHandler) { BaseAddress = new Uri("https://lms.invalid/") };
var practiceOptions = new LotusLmsOptions { LogApiResponses = false, WebOrigin = "https://lms.invalid" };
var practiceApi = new LotusLmsApiClient(practiceHttp, Options.Create(practiceOptions));
var job = CreatePracticeJob(practiceApi, practiceOptions, clock);
var configuredWorkerOptions = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Worker:Mode"] = "PracticeOnly",
    ["Worker:PracticeBatchSize"] = "5",
    ["Worker:PracticeIntervalSeconds"] = "3",
    ["Worker:PracticeTargets:0:CourseIid"] = "22",
    ["Worker:PracticeTargets:0:PracticeIid"] = "11",
}).Build().GetSection("Worker").Get<WorkerOptions>()!;
Check(configuredWorkerOptions.Mode == WorkerMode.PracticeOnly && configuredWorkerOptions.PracticeTargets.Single().IsValid,
    "practice-only configuration needs only course and practice IDs");
var report = await job.RunAsync(
    new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
    configuredWorkerOptions, CancellationToken.None);
var saves = practiceHandler.Requests.Where(r => r.Path == "/practice/api/save-take").ToArray();
Check(practiceHandler.Requests.Select(r => r.Path).SequenceEqual(new[] {
    "/user/login", "/practice/api/recalculate-course-practice-progress", "/practice/api/get-practice-detail",
    "/practice/api/get-question-bank-status", "/practice/api/get-sync-practice-progress", "/practice/api/start-practice",
    "/practice/api/save-take", "/practice/api/save-take", "/time-tracking/api/track-time"
}), "practice-only follows the HAR lifecycle and flushes measured practice time");
Check(practiceHandler.Requests[3].Method == HttpMethod.Post, "direct practice reads bank using POST");
Check(saves.Length == 2, "practice-only processes a configured target in two batches");
Check(saves[0].Form.Keys.Count(k => k.StartsWith("take_info[") && k.EndsWith("][iid]")) == 5
    && saves[1].Form.Keys.Count(k => k.StartsWith("take_info[") && k.EndsWith("][iid]")) == 1,
    "six questions are sent as 5 plus the final 1");
Check(practiceHandler.PracticeSaveTicks.SequenceEqual(new[] { TimeSpan.FromSeconds(3).Ticks, TimeSpan.FromSeconds(6).Ticks }),
    "first batch waits 3s and submissions remain 3s apart");
Check(saves[0].Form["take_id"] == "fresh-take" && saves[1].Form["take_id"] == "next-take",
    "start-practice take id is used and the save response id carries into the next batch");
Check(saves.Select(s => s.Form["time_start"]).Distinct().Count() == 1, "take start time remains stable");
Check(saves.All(s => s.Form["finished"] == "0"), "all batches keep finished=0");
Check(saves.All(s => s.Form["course_iid"] == "22" && s.Form["practice_iid"] == "11"
    && s.Form["exam_sco_iid"] == "33"), "save batches use course/practice IDs and fresh exam metadata");
Check(practiceHandler.Requests[3].Fields["bank_iids[]"] == "55"
    && practiceHandler.Requests[3].Fields["last_ts"] == "0", "bank query uses configured bank and last_ts=0");
Check(saves.All(s => s.Form["question_category"] == "Văn hóa, đạo đức nghề nghiệp người lái xe"),
    "category is taken from UTF-8 question tags");
Check(practiceHandler.Requests.Single(r => r.Path == "/practice/api/get-question-bank-status").Fields["exam_template_iid"] == "33",
    "question bank uses exam_sco iid rather than the nested template iid");
Check(report.QuestionsSubmitted == 6 && report.BatchesSubmitted == 2
    && report.Courses.Single().Result.AlreadyCorrect == 0, "standalone practice report counts questions and batches");
Check(practiceHandler.Requests.All(r => !r.Path.Contains("save-item-learning-time")),
    "practice-only never sends video learning mutations");
var tracking = practiceHandler.Requests.Single(r => r.Path.EndsWith("track-time"));
Check(tracking.Form["page_id"] == "practice-exam" && tracking.Form["item_id"] == "11"
    && tracking.Form["metadata[user_iid]"] == "456789" && tracking.Form["time_spent"] == "6.000",
    "practice tracking uses the practice iid, user metadata and measured elapsed time");
Check(saves.All(s => double.Parse(s.Form["time_used"], CultureInfo.InvariantCulture) == 3),
    "batch time is measured from the scheduler rather than randomized");
foreach (var save in saves)
{
    var questionIds = save.Form.Where(kv => kv.Key.StartsWith("take_info[") && kv.Key.EndsWith("][iid]"));
    foreach (var question in questionIds)
    {
        var prefix = question.Key[..^5];
        Check(double.Parse(save.Form[prefix + "[spentTime]"], CultureInfo.InvariantCulture)
            == int.Parse(save.Form[$"spent_time_each_question[{question.Value}][doing]"], CultureInfo.InvariantCulture) / 1000d
            && save.Form[$"spent_time_each_question[{question.Value}][viewkey]"] == "0",
            "per-question time equals measured doing time and does not invent answer-panel time");
    }
}

var failureClock = new AdvancingTimeProvider();
using var failureHandler = new CaptureHandler { PracticeScenario = true, ScheduleClock = failureClock, RejectPracticeSave = true };
using var failureHttp = new HttpClient(failureHandler) { BaseAddress = new Uri("https://lms.invalid/") };
var failureJob = CreatePracticeJob(new LotusLmsApiClient(failureHttp, Options.Create(practiceOptions)),
    practiceOptions, failureClock);
try
{
    await failureJob.RunAsync(new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
        new WorkerOptions { Mode = WorkerMode.PracticeOnly, PracticeTargets = [FixtureTarget()] }, CancellationToken.None);
    throw new Exception("Expected rejected save to stop practice job");
}
catch (LotusLmsApiException)
{
    Check(failureHandler.PracticeSaveTicks.Count == 1, "rejected mutation stops remaining batches without retry");
}

using var unconfiguredHandler = new CaptureHandler { PracticeScenario = true, PracticeOnlyScenario = true };
using var unconfiguredHttp = new HttpClient(unconfiguredHandler) { BaseAddress = new Uri("https://lms.invalid/") };
var unconfiguredJob = CreatePracticeJob(new LotusLmsApiClient(unconfiguredHttp, Options.Create(practiceOptions)),
    practiceOptions, new AdvancingTimeProvider());
foreach (var targets in new[] { Array.Empty<PracticeTargetOptions>(), new[] { new PracticeTargetOptions
    { CourseIid = 22, PracticeIid = 0 } } })
{
    try
    {
        await unconfiguredJob.RunAsync(
            new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
            new WorkerOptions { Mode = WorkerMode.PracticeOnly, PracticeTargets = targets }, CancellationToken.None);
        throw new Exception("Expected missing course/practice IDs to stop before any API call");
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("PracticeTargets"))
    {
        Check(unconfiguredHandler.Requests.Count == 0, "missing target metadata stops before login or mutation");
    }
}

foreach (var rejectBank in new[] { false, true })
{
    using var bankHandler = new CaptureHandler
    {
        PracticeScenario = true, PracticeOnlyScenario = true, EmptyQuestionBank = true, RejectQuestionBank = rejectBank
    };
    using var bankHttp = new HttpClient(bankHandler) { BaseAddress = new Uri("https://lms.invalid/") };
    var bankJob = CreatePracticeJob(new LotusLmsApiClient(bankHttp, Options.Create(practiceOptions)),
        practiceOptions, new AdvancingTimeProvider());
    try
    {
        var bankReport = await bankJob.RunAsync(
            new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
            new WorkerOptions { PracticeTargets = [FixtureTarget()] }, CancellationToken.None);
        Check(!rejectBank && bankReport.QuestionsSubmitted == 0 && bankReport.BatchesSubmitted == 0,
            "empty bank returns a zero-submission report");
    }
    catch (LotusLmsApiException) when (rejectBank)
    {
        checks++;
    }
    Check(bankHandler.Requests.Select(r => r.Path).SequenceEqual(new[] { "/user/login", "/practice/api/recalculate-course-practice-progress", "/practice/api/get-practice-detail", "/practice/api/get-question-bank-status" }),
        "empty or rejected bank never starts a take, syncs, saves, or retries");
}

var studyClock = new AdvancingTimeProvider();
using var studyHandler = new CaptureHandler { PracticeScenario = true, CoursePassed = false, ScheduleClock = studyClock };
using var studyHttp = new HttpClient(studyHandler) { BaseAddress = new Uri("https://lms.invalid/") };
var studyApi = new LotusLmsApiClient(studyHttp, Options.Create(practiceOptions));
var studyJob = new LotusLmsAutoStudy(studyApi,
    new LotusLmsAccountReader(studyApi, Options.Create(practiceOptions), NullLogger<LotusLmsAccountReader>.Instance),
    new LotusLmsPractice(studyApi, NullLogger<LotusLmsPractice>.Instance, studyClock),
    NullLogger<LotusLmsAutoStudy>.Instance);
var studyReport = await studyJob.RunAsync(
    new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
    new WorkerOptions { Mode = WorkerMode.AutoStudy }, CancellationToken.None);
Check(studyReport.CoursesProcessed == 1 && studyReport.Courses.Single().PracticeOutcome is null
    && studyHandler.PracticeSaveTicks.Count == 0,
    "auto-study keeps practice disabled by default after extraction");
var combinedReport = await studyJob.RunAsync(
    new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
    new WorkerOptions { Mode = WorkerMode.AutoStudy, IncludePractice = true }, CancellationToken.None);
Check(combinedReport.CoursesProcessed == 1 && combinedReport.Courses.Single().PracticeOutcome is not null
    && studyHandler.PracticeSaveTicks.Count == 2,
    "auto-study IncludePractice reuses the extracted practice engine");

var harFixture = JsonNode.Parse(File.ReadAllText(
    Path.Combine(AppContext.BaseDirectory, "Fixtures/truongson-practice.json")))!.AsObject();
var harClock = new AdvancingTimeProvider();
using var harHandler = new CaptureHandler
{
    PracticeScenario = true, PracticeOnlyScenario = true,
    ScheduleClock = harClock, HarResponses = harFixture["responses"]!.AsObject()
};
using var harHttp = new HttpClient(harHandler) { BaseAddress = new Uri("https://lms.invalid/") };
var harJob = CreatePracticeJob(new LotusLmsApiClient(harHttp, Options.Create(practiceOptions)), practiceOptions, harClock);
var harReport = await harJob.RunAsync(
    new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
    new WorkerOptions { PracticeTargets = [FixtureTarget()] }, CancellationToken.None);
var harSaves = harHandler.Requests.Where(r => r.Path == "/practice/api/save-take").ToArray();
Check(harReport.QuestionsSubmitted == 25 && harSaves.Length == 5
    && harReport.Courses.Single().Result.AlreadyCorrect == 25,
    "HAR history with prior correct counts does not skip questions or imply KPI completion");
Check(harSaves.All(r => r.Form["take_id"] == "har-fresh-take" && r.Form["exam_sco_iid"] == "33"),
    "stale configured exam, bank and take IDs are replaced by fresh server metadata");
Check(harReport.GainedKpiSeconds == 0 && harReport.Courses.Single().Result.AlreadyOvertime,
    "HAR overtime response reports zero KPI credit despite successful saves");
Check(harHandler.Requests.Where(r => r.Path != "/user/login").All(r => r.MediaType == "multipart/form-data"),
    "practice lifecycle requests use multipart form data like the HAR");
Check(harHandler.Requests.Where(r => r.Path is not "/user/login" and not "/practice/api/get-practice-detail"
    and not "/practice/api/get-question-bank-status").All(r => r.Query.Length == 0 && r.Form["lang"] == "vn"),
    "sync, start, recalculate, tracking and save flags are in the body without a URL query");
Check(harHandler.Requests.Where(r => r.Path.EndsWith("track-time"))
    .Select(r => r.Form["time_spent"]).SequenceEqual(new[] { "10.000", "5.000" }),
    "HAR practice heartbeat sends measured 10s intervals plus the final remainder");
foreach (var request in harHandler.Requests) CheckPair(request, request.Path == "/user/login" ? null : session.UserIid);

// Verify that the typed API also preserves the actual mixed answer outcomes and
// millisecond timings from HAR save request 113, independently of auto-answering.
var harForm = harFixture["save_form"]!.Deserialize<Dictionary<string, string>>()!;
using var payloadHandler = new CaptureHandler();
using var payloadHttp = new HttpClient(payloadHandler) { BaseAddress = new Uri("https://lms.invalid/") };
var payloadApi = new LotusLmsApiClient(payloadHttp, Options.Create(practiceOptions));
await payloadApi.SavePracticeTakeAsync(session, new SavePracticeTakeRequest
{
    CourseIid = 22, PracticeIid = 11, ExamScoIid = 33, TakeId = harForm["take_id"],
    Finished = false, TimeStartUnixSeconds = long.Parse(harForm["time_start"], CultureInfo.InvariantCulture),
    TimeUsedSeconds = double.Parse(harForm["time_used"], CultureInfo.InvariantCulture),
    Score = decimal.Parse(harForm["score"], CultureInfo.InvariantCulture),
    PaperScore = decimal.Parse(harForm["paper_score"], CultureInfo.InvariantCulture),
    QuestionCategory = harForm["question_category"],
    Items = Enumerable.Range(0, 5).Select(i => new PracticeTakeItem
    {
        QuestionId = harForm[$"take_info[{i}][iid]"], Correct = harForm[$"take_info[{i}][correct]"] == "1",
        SpentSeconds = double.Parse(harForm[$"take_info[{i}][spentTime]"], CultureInfo.InvariantCulture),
        Score = decimal.Parse(harForm[$"take_info[{i}][score]"], CultureInfo.InvariantCulture),
        Percent = decimal.Parse(harForm[$"take_info[{i}][percent]"], CultureInfo.InvariantCulture),
        DoingMilliseconds = int.Parse(harForm[$"spent_time_each_question[{harForm[$"take_info[{i}][iid]"]}][doing]"], CultureInfo.InvariantCulture),
        ViewKeyMilliseconds = int.Parse(harForm[$"spent_time_each_question[{harForm[$"take_info[{i}][iid]"]}][viewkey]"], CultureInfo.InvariantCulture)
    }).ToArray()
});
foreach (var field in harForm)
{
    var actual = payloadHandler.Requests.Single().Form[field.Key];
    Check(actual == field.Value || (double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualNumber)
        && double.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber)
        && Math.Abs(actualNumber - expectedNumber) < 0.000001), "HAR save payload preserves " + field.Key);
}

foreach (var missingId in new[] { false, true })
{
    var startClock = new AdvancingTimeProvider();
    using var startHandler = new CaptureHandler
    {
        PracticeScenario = true, RejectStart = !missingId, MissingStartId = missingId, ScheduleClock = startClock
    };
    using var startHttp = new HttpClient(startHandler) { BaseAddress = new Uri("https://lms.invalid/") };
    var startJob = CreatePracticeJob(new LotusLmsApiClient(startHttp, Options.Create(practiceOptions)), practiceOptions, startClock);
    try
    {
        await startJob.RunAsync(new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
            new WorkerOptions { PracticeTargets = [FixtureTarget()] }, CancellationToken.None);
        throw new Exception("Expected invalid start-practice to stop the job");
    }
    catch (Exception exception) when (exception is LotusLmsApiException or InvalidDataException)
    {
        Check(startHandler.PracticeSaveTicks.Count == 0
            && startHandler.Requests.Count(r => r.Path.EndsWith("start-practice")) == 1,
            "failed or malformed start never saves with a historical/configured take ID or retries");
    }
}

foreach (var missingMetadata in new[] { false, true })
{
    var malformedClock = new AdvancingTimeProvider();
    using var malformedHandler = new CaptureHandler
    {
        PracticeScenario = true, MissingPracticeMetadata = missingMetadata,
        NoAnswerKeys = !missingMetadata, ScheduleClock = malformedClock
    };
    using var malformedHttp = new HttpClient(malformedHandler) { BaseAddress = new Uri("https://lms.invalid/") };
    var malformedJob = CreatePracticeJob(new LotusLmsApiClient(malformedHttp, Options.Create(practiceOptions)), practiceOptions, malformedClock);
    try
    {
        await malformedJob.RunAsync(new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
            new WorkerOptions { PracticeTargets = [FixtureTarget()] }, CancellationToken.None);
        throw new Exception("Expected incomplete metadata or answer keys to stop the job");
    }
    catch (InvalidDataException)
    {
        Check(malformedHandler.PracticeSaveTicks.Count == 0,
            "missing metadata or answer keys never falls back to stale IDs or fabricated results");
    }
}

var finishClock = new AdvancingTimeProvider();
using var finishHandler = new CaptureHandler { PracticeScenario = true, FinishAutomatically = true, ScheduleClock = finishClock };
using var finishHttp = new HttpClient(finishHandler) { BaseAddress = new Uri("https://lms.invalid/") };
var finishJob = CreatePracticeJob(new LotusLmsApiClient(finishHttp, Options.Create(practiceOptions)), practiceOptions, finishClock);
var finishReport = await finishJob.RunAsync(
    new LotusLmsCredentialsOptions { Username = "fixture-user", Password = "fixture-password" },
    new WorkerOptions { PracticeTargets = [FixtureTarget()] }, CancellationToken.None);
Check(finishHandler.PracticeSaveTicks.Count == 1 && finishReport.QuestionsSubmitted == 5
    && finishReport.GainedKpiSeconds == 2.5 && finishReport.Courses.Single().Result.ServerFinished,
    "server auto-finish stops subsequent batches and records only returned KPI credit");

var heartbeatClock = new AdvancingTimeProvider();
var heartbeats = new List<double>();
var longSchedule = new PracticeBatchSchedule(TimeSpan.FromSeconds(35), heartbeatClock,
    (elapsed, _) => { heartbeats.Add(elapsed); return Task.CompletedTask; });
await longSchedule.WaitAsync(CancellationToken.None);
await longSchedule.FlushTrackingAsync(CancellationToken.None);
Check(heartbeats.SequenceEqual(new[] { 10d, 10d, 10d, 5d }), "long batch waits keep 10s heartbeats without inventing time");

using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await new PracticeBatchSchedule(TimeSpan.FromSeconds(3), clock).WaitAsync(cancelled.Token);
    throw new Exception("Expected cancelled wait");
}
catch (OperationCanceledException) { checks++; }

Console.WriteLine($"PASS: {checks} checks (frontend crypto/sync vectors, concurrency, POST signing, HAR practice lifecycle/payloads, account isolation, legacy replay).");

static PracticeTargetOptions FixtureTarget() => new()
{
    CourseIid = 22, PracticeIid = 11, ExamScoIid = 999, BankIid = 999,
    TakeId = "stale-take", Name = "Fixture"
};

static LotusLmsPracticeOnly CreatePracticeJob(ILotusLmsApiClient api, LotusLmsOptions options, TimeProvider clock) =>
    new(new LotusLmsAccountReader(api, Options.Create(options), NullLogger<LotusLmsAccountReader>.Instance),
        new LotusLmsPractice(api, NullLogger<LotusLmsPractice>.Instance, clock),
        clock, NullLogger<LotusLmsPracticeOnly>.Instance);

void CheckPair(CapturedRequest request, int? iid, string authenticationToken = "fixture-token")
{
    Check(request.Fields.ContainsKey("_sand_ri") && request.Fields.ContainsKey("_sand_rit"), "request has both signing fields");
    Check(Decrypt(request.Fields["_sand_rit"], iid) == request.Fields["_sand_ri"], "request uses correct user/guest seed");
    Check(!request.Fields["_sand_ri"].Contains("stale"), "captured request id is ignored by default");
    if (iid is null)
    {
        Check(!request.Fields.ContainsKey("_sand_client_sync_token"), "guest login omits captured sync token");
    }
    else
    {
        var sync = request.Fields["_sand_client_sync_token"];
        Check(Enumerable.Range(0, 2).Any(offset => sync == SandClientSyncTokenGenerator.Generate(
            authenticationToken, request.TimestampUnixSeconds - offset)), "authenticated request has a fresh token-bound timestamp");
    }
}

static string Decrypt(string token, int? userIid)
{
    var number = userIid.HasValue ? (2L * userIid + 2451).Value.ToString(CultureInfo.InvariantCulture) : "NaN";
    var secret = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(number + " t i a v s t"))).ToLowerInvariant();
    var parts = token.Split(':');
    using var aes = Aes.Create();
    aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    return Encoding.UTF8.GetString(aes.DecryptCbc(Convert.FromBase64String(parts[1]), Convert.FromHexString(parts[0]), PaddingMode.PKCS7));
}

internal sealed record CryptoJsVector(int? UserIid, string Uuid, long TimestampMilliseconds, string IvHex, string RequestId, string RequestToken);
internal sealed record ClientSyncVector(string AuthenticationToken, long TimestampUnixSeconds, string ClientSyncToken);
internal sealed record CapturedRequest(HttpMethod Method, Dictionary<string, string> Form, Dictionary<string, string> Fields, long TimestampUnixSeconds, string Path, string? MediaType, string Query);

internal sealed class CaptureHandler : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];
    public bool PracticeScenario { get; init; }
    public bool PracticeOnlyScenario { get; init; }
    public JsonObject? HarResponses { get; init; }
    public bool RejectStart { get; init; }
    public bool MissingStartId { get; init; }
    public bool FinishAutomatically { get; init; }
    public bool MissingPracticeMetadata { get; init; }
    public bool NoAnswerKeys { get; init; }
    public bool EmptyQuestionBank { get; init; }
    public bool RejectQuestionBank { get; init; }
    public bool CoursePassed { get; init; } = true;
    public bool RejectPracticeSave { get; init; }
    public TimeProvider? ScheduleClock { get; init; }
    public List<long> PracticeSaveTicks { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        static Dictionary<string, string> Parse(string text) => text.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .GroupBy(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')))
            .ToDictionary(group => group.Key,
                group => Uri.UnescapeDataString((group.Last().Length == 2 ? group.Last()[1] : "").Replace('+', ' ')));

        var form = new Dictionary<string, string>();
        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (var part in multipart)
            {
                form.Add(part.Headers.ContentDisposition!.Name!.Trim('"'), await part.ReadAsStringAsync(cancellationToken));
            }
        }
        else
        {
            form = Parse(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        }
        var query = Parse(request.RequestUri!.Query);
        var fields = new Dictionary<string, string>(query);
        foreach (var field in form) fields[field.Key] = field.Value;
        var path = request.RequestUri.AbsolutePath;
        if (PracticeOnlyScenario && path is not "/user/login" and not "/practice/api/get-question-bank-status"
            and not "/practice/api/get-practice-detail" and not "/practice/api/recalculate-course-practice-progress"
            and not "/practice/api/get-sync-practice-progress" and not "/practice/api/start-practice"
            and not "/practice/api/save-take" and not "/time-tracking/api/track-time")
        {
            throw new Exception("Unexpected endpoint in practice-only: " + path);
        }
        Requests.Add(new(request.Method, form, fields, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), path, request.Content?.Headers.ContentType?.MediaType, request.RequestUri.Query));
        if (path == "/practice/api/save-take" && ScheduleClock is not null) PracticeSaveTicks.Add(ScheduleClock.GetTimestamp());
        var response = path == "/user/login"
            ? """{"success":true,"result":{"pass":1,"token":"fixture-token","iid":456789,"id":"fixture-id","lname":"fixture-user","name":"Fixture"}}"""
            : request.RequestUri.AbsolutePath == "/time-tracking/course-time-tracking/get-detail-learning-time"
                ? """{"success":true,"result":{"iid":22,"ntype":"syllabus","children":[{"iid":11,"ntype":"practice","actual_learning_time":"3.5","children":[{"iid":12,"ntype":"video","actual_learning_time":1.25}]}]}}"""
                : """{"success":true,"result":{}}""";
        if (PracticeScenario && path != "/user/login")
        {
            response = path switch
            {
                "/student/api/get-student-enrolment-plans" => """{"success":true,"result":[{"iid":99}]}""",
                "/student/api/get-student-courses-in-enrolment-plan-table-view" => JsonSerializer.Serialize(new
                {
                    success = true,
                    result = new[] { new { course = new { iid = 22, name = "Fixture",
                        __expand = new { score_by_rubric = new { passed = CoursePassed,
                            rubric = new { pass_score = 8, max_score = 10 } } } } } }
                }),
                "/practice/api/get-practice-detail" => MissingPracticeMetadata
                    ? """{"success":true,"result":{}}"""
                    : """{"success":true,"result":{"_expand":{"exam_sco":{"iid":33,"question_banks":[55],"template_details":[{"exercise_template":[{"exam_template_iid":44}]}]}}}}""",
                "/practice/api/get-question-bank-status" => RejectQuestionBank
                    ? """{"success":false,"message":"fixture bank rejection"}"""
                    : JsonSerializer.Serialize(new
                {
                    success = true,
                    result = new { questions = new { result = Enumerable.Range(1, EmptyQuestionBank ? 0 : 6).Select(i => new
                    {
                        iid = "fixture-question-" + i,
                        tags = new[] { "Văn hóa, đạo đức nghề nghiệp người lái xe" },
                        mc_answers = new[] { new { is_answer = NoAnswerKeys ? 0 : 1 } }
                    }) } }
                }),
                "/practice/api/get-sync-practice-progress" => """{"success":true,"result":{"id":"history-is-not-a-take","sync_data":[]}}""",
                "/practice/api/start-practice" => RejectStart
                    ? """{"success":false,"message":"fixture start rejection"}"""
                    : MissingStartId ? """{"success":true,"result":{}}"""
                    : """{"success":true,"result":{"id":"fresh-take","timestart":1792200005}}""",
                "/practice/api/save-take" => RejectPracticeSave
                    ? """{"success":false,"message":"fixture rejection"}"""
                    : """{"success":true,"result":{"take_id":"next-take"}}""",
                _ => response
            };
            if (HarResponses?[path] is JsonObject harResponse) response = harResponse.ToJsonString();
            if (FinishAutomatically && path == "/practice/api/save-take")
                response = """{"success":true,"result":{"id":"fresh-take","take_is_finished_automatically_by_server":1,"you_have_just_gained":2.5}}""";
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    }
}

internal sealed class AdvancingTimeProvider : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1_792_200_000).AddTicks(_ticks);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _ticks += dueTime.Ticks;
        callback(state);
        return new CompletedTimer();
    }

    private sealed class CompletedTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
