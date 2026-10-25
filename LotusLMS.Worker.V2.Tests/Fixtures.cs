using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Worker.V2;
using LotusLMS.Worker.V2.Accounts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal sealed class Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    public const string WorkerName = "fixture-worker";
    public static readonly string AccountId = Guid.NewGuid().ToString();
    public static readonly string InstanceId = Guid.NewGuid().ToString();
    public static readonly string RunId = Guid.NewGuid().ToString();
    public static readonly DateTimeOffset ServerTime = DateTimeOffset.Parse("2040-01-01T00:00:00Z");
    public static IOptions<WorkerOptions> Options => Microsoft.Extensions.Options.Options.Create(new WorkerOptions
        { WorkerName = WorkerName, HeartbeatIntervalSeconds = 1, PollIntervalSeconds = 1 });
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => responder(request, token);
    public LotusClient Client() => new(new HttpClient(this), Microsoft.Extensions.Options.Options.Create(new LotusClientOptions
        { BaseUrl = "http://backend.invalid", ApiKey = "fixture-api-secret", RequestTimeoutSeconds = 2 }));
    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    public static HttpResponseMessage Error(string code, HttpStatusCode status = HttpStatusCode.Conflict) => Json(new { error = new { code } }, status);
    public static JsonObject Assignment(JsonObject? claim = null, string? accountId = null) => new()
    {
        ["accountId"] = accountId ?? AccountId, ["workerName"] = WorkerName,
        ["instanceId"] = claim?["instanceId"]?.GetValue<string>() ?? InstanceId, ["runId"] = RunId,
        ["leaseToken"] = "fixture-lease-secret", ["status"] = "running", ["state"] = "accepted",
        ["serverTime"] = ServerTime, ["leaseExpiresAt"] = ServerTime.AddSeconds(30),
        ["enrolmentPlanIid"] = 99, ["enrolmentPlanName"] = "Chương trình A", ["provider"] = "truongson",
        ["credentials"] = new JsonObject { ["username"] = "000000000001", ["password"] = "fixture-student-secret", ["deviceUuid"] = AccountId },
        ["lms"] = new JsonObject { ["base"] = "https://lms.invalid", ["tenant"] = "truongson", ["origin"] = "https://truongson.invalid" }
    };
    public static ClientAssignment Read(JsonObject value) => value.Deserialize<ClientAssignment>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    public static AccountRunContext Context(JsonObject? value = null, TimeProvider? clock = null, long? started = null)
    {
        clock ??= TimeProvider.System;
        var assignment = Read(value ?? Assignment());
        return new AccountRunContext(assignment, assignment.Proxy, AccountId, WorkerName, InstanceId,
            started ?? clock.GetTimestamp(), clock, "000000000001");
    }
    public static HttpResponseMessage AccountResponse(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath == "/api/worker/accounts")
            return Json(new { data = new[] { new { id = AccountId, identityNumber = "000000000001", status = "running" } } });
        if (request.RequestUri.AbsolutePath == $"/api/accounts/{AccountId}")
            return Json(new { data = new { id = AccountId, identityNumber = "000000000001", proxyId = (string?)null } });
        throw new NotSupportedException($"Unexpected client API GET: {request.RequestUri.AbsolutePath}");
    }
    public static AccountReceiver Receiver(
        ILotusClient client,
        TimeProvider? clock = null,
        AccountWebProxyProvider? proxyProvider = null) =>
        new(client, WorkerName, clock ?? TimeProvider.System, proxyProvider ?? new AccountWebProxyProvider(),
            NullLogger<AccountReceiver>.Instance);
    public static async Task<JsonObject> Body(HttpRequestMessage request) => JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
}

internal class TheoryClientProxy : DispatchProxy
{
    private int _minimumSeconds;
    private bool _kpiFull;
    private double _actualSeconds;
    private ILotusLmsApiClient _client = null!;

    public TheoryClientProxy() { }

    public List<SaveItemLearningTimeRequest> SavedItems { get; } = [];
    public int TrackCalls { get; private set; }
    public ILotusLmsApiClient Client => _client;

    public static TheoryClientProxy Create(int minimumSeconds, bool kpiFull = false)
    {
        var client = DispatchProxy.Create<ILotusLmsApiClient, TheoryClientProxy>();
        var proxy = (TheoryClientProxy)(object)client;
        proxy._client = client;
        proxy._minimumSeconds = minimumSeconds;
        proxy._kpiFull = kpiFull;
        return proxy;
    }

    protected override object Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        return targetMethod?.Name switch
        {
            nameof(ILotusLmsApiClient.GetDetailLearningTimeAsync) =>
                Task.FromResult(new BaseApi<ResultDetailLearningTime>
                {
                    Result = new ResultDetailLearningTime
                    {
                        Iid = 99,
                        Children =
                        [
                            new LearningTimeNode
                            {
                                Iid = 44,
                                Ntype = "video",
                                Name = "Theory video",
                                MinLearningTime = _minimumSeconds,
                                ActualLearningTime = _actualSeconds
                            }
                        ]
                    }
                }),
            nameof(ILotusLmsApiClient.SaveItemLearningTimeAsync) => SaveAsync((SaveItemLearningTimeRequest)args![1]!),
            nameof(ILotusLmsApiClient.GetTodayKpiTimeAsync) => Task.FromResult(new GetToDayKpiTime
            {
                TodayGainedKpiTime = new TodayGainedKpiTime { Full = _kpiFull }
            }),
            nameof(ILotusLmsApiClient.TrackTimeAsync) => TrackTimeAsync(),
            _ => throw new NotSupportedException($"Unexpected LMS API call: {targetMethod?.Name}")
        };
    }

    private Task<JsonObject> SaveAsync(SaveItemLearningTimeRequest request)
    {
        SavedItems.Add(request);
        _actualSeconds += request.TimeSpentSeconds;
        return Task.FromResult(new JsonObject());
    }

    private Task<JsonObject> TrackTimeAsync()
    {
        TrackCalls++;
        return Task.FromResult(new JsonObject());
    }
}

internal sealed class ManualClock : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private long _timestamp;
    private DateTimeOffset _utc = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public override DateTimeOffset GetUtcNow() => _utc;
    public void JumpWallClock(TimeSpan duration) => _utc += duration;
    public void Advance(TimeSpan duration)
    {
        _timestamp += duration.Ticks; _utc += duration;
        foreach (var timer in _timers.ToArray()) timer.FireIfDue();
    }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state); _timers.Add(timer); timer.Change(dueTime, period); return timer;
    }
    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private long _due = long.MaxValue;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        { _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.GetTimestamp() + dueTime.Ticks; return true; }
        public void FireIfDue() { if (_due <= clock.GetTimestamp()) { _due = long.MaxValue; callback(state); } }
        public void Dispose() { _due = long.MaxValue; clock._timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
