using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Worker.V2;
using LotusLMS.Worker.V2.Accounts;
using LotusLMS.Worker.V2.LotusRunner.CheckingTime;
using LotusLMS.Worker.V2.LotusRunner.Theory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    checks++;
}
async Task Throws<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); } catch (T) { Check(true, message); return; }
    throw new Exception("FAIL: " + message);
}

Check(!new WorkerOptions().IsValid(), "worker name is required");
Check(!new WorkerOptions
{
    WorkerName = "a",
    MaxConcurrentAccounts = 1,
    RegistrationApiKey = "fixture-bootstrap-key-0123456789abcdef",
    HeartbeatIntervalSeconds = 30
}.IsValid(), "heartbeat stays inside backend lease");
var validLaneOptions = new WorkerOptions
{
    WorkerName = "worker-01",
    MaxConcurrentAccounts = 2,
    RegistrationApiKey = "fixture-bootstrap-key-0123456789abcdef",
    PollIntervalSeconds = 5,
    HeartbeatIntervalSeconds = 5
};
Check(validLaneOptions.IsValid(),
    "minimal worker options are valid");
Check(validLaneOptions.GetLaneWorkerName(1) == "worker-01-01"
    && validLaneOptions.GetLaneWorkerName(2) == "worker-01-02",
    "lane names include the configured worker prefix and a two-digit suffix");
Check(!new WorkerOptions
{
    WorkerName = "worker-01",
    MaxConcurrentAccounts = 2,
    RegistrationApiKey = "",
    PollIntervalSeconds = 5,
    HeartbeatIntervalSeconds = 5
}.IsValid(), "worker bootstrap registration key is required");
var validation = new WorkerOptions
{
    WorkerName = "",
    RegistrationApiKey = "fixture-bootstrap-key-0123456789abcdef",
    PollIntervalSeconds = 5,
    HeartbeatIntervalSeconds = 5
}.ValidationErrors().ToArray();
Check(validation.Single().Contains("Worker:WorkerName"), "missing worker name identifies its exact configuration field");
validation = new WorkerOptions
{
    WorkerName = "worker-01",
    MaxConcurrentAccounts = 1,
    RegistrationApiKey = "fixture-bootstrap-key-0123456789abcdef",
    PollIntervalSeconds = 0,
    HeartbeatIntervalSeconds = 30
}.ValidationErrors().ToArray();
Check(validation.Length == 2 && validation.Any(error => error.Contains("Worker:HeartbeatIntervalSeconds")),
    "validation lists independent interval errors");

var theorySession = new LotusLmsSession
{
    Token = "fixture-token",
    UserIid = 22,
    UserId = "fixture-user",
    LoginName = "fixture-user",
    DisplayName = "Fixture User",
    DeviceUuid = Guid.NewGuid().ToString(),
    SessionId = Guid.NewGuid().ToString()
};
var theoryClient = TheoryClientProxy.Create(minimumSeconds: 1);
var theoryLogs = new List<string>();
var theoryService = new TheoryServices(
    theoryClient.Client,
    TimeProvider.System,
    Options.Create(new WorkerOptions { SaveItemSeconds = 1 }),
    NullLogger<TheoryServices>.Instance);
var theoryResult = await theoryService.RunAsync(
    theorySession,
    [
        new CheckingTimeDTO.AutoStudy
        {
            iid = 44,
            iidRoot = 99,
            name = "Theory video",
            ntype = "video",
            typeAutoStudy = CheckingTimeDTO.TypeAutoStudy.AutoStudy
        },
        new CheckingTimeDTO.AutoStudy
        {
            iid = 45,
            iidRoot = 99,
            name = "Practice must be ignored",
            typeAutoStudy = CheckingTimeDTO.TypeAutoStudy.PracticeOnly
        }
    ],
    () => Task.FromResult(new StudyPlan([], IsCompleted: true)),
    (message, _) => { theoryLogs.Add(message); return Task.CompletedTask; },
    default);
Check(theoryResult == AccountStudyResult.Completed && theoryClient.SavedItems.Count == 1
    && theoryClient.SavedItems[0].ItemIid == 44,
    "Theory processes planned videos and refreshes LMS progress before completion");
Check(theoryLogs.Any(message => message.Contains("hoàn thành bài lý thuyết")),
    "Theory progress is sent to the client API");

var kpiTheoryClient = TheoryClientProxy.Create(minimumSeconds: 2, kpiFull: true);
var kpiTheoryService = new TheoryServices(
    kpiTheoryClient.Client,
    TimeProvider.System,
    Options.Create(new WorkerOptions
    {
        SaveItemSeconds = 1,
        StopOnDailyKpi = true,
        KpiCheckEverySaves = 1
    }),
    NullLogger<TheoryServices>.Instance);
var kpiResult = await kpiTheoryService.RunAsync(
    theorySession,
    [
        new CheckingTimeDTO.AutoStudy
        {
            iid = 44,
            iidRoot = 99,
            name = "Theory video",
            ntype = "video",
            typeAutoStudy = CheckingTimeDTO.TypeAutoStudy.AutoStudy
        }
    ],
    () => throw new Exception("KPI stop must not refresh the study plan."),
    (_, _) => Task.CompletedTask,
    default);
Check(kpiResult == AccountStudyResult.DailyKpiReached && kpiTheoryClient.SavedItems.Count == 1
    && kpiTheoryClient.TrackCalls == 0,
    "Theory stops further saves when its configured KPI check is reached");

// A committed claim whose response was lost must retain its account, instance and request IDs.
var claims = new List<JsonObject>(); var gets = 0;
using (var transport = new Fixture(async (request, token) =>
{
    if (request.Method == HttpMethod.Get)
    {
        if (request.RequestUri!.AbsolutePath == "/api/worker/accounts") gets++;
        return Fixture.AccountResponse(request);
    }
    var body = await Fixture.Body(request); claims.Add(body);
    if (claims.Count == 1) throw new HttpRequestException("fixture connection reset");
    return Fixture.Json(new { data = Fixture.Assignment(body) });
}))
using (var client = transport.Client())
{
    var receiver = Fixture.Receiver(client);
    await Throws<LotusClientException>(async () => { using var unused = await receiver.ReceiveAsync(default); }, "lost claim response surfaces as a backend failure");
    using var accepted = await receiver.ReceiveAsync(default);
    Check(accepted?.EnrolmentPlanIid == 99 && accepted.ValidateConfiguration() is null, "claim prepares complete account context");
    Check(gets == 1 && JsonNode.DeepEquals(claims[0], claims[1]), "lost response retries the exact same claim without listing another account");
    using var reconciled = await receiver.ReceiveAsync(default);
    Check(JsonNode.DeepEquals(claims[1], claims[2]) && gets == 1, "unacknowledged finish is reconciled before any new account");
    receiver.Release();
    using var next = await receiver.ReceiveAsync(default);
    Check(gets == 2 && claims[3]["requestId"]!.ToString() != claims[2]["requestId"]!.ToString(), "confirmed release permits a new request");
    Check(claims[3]["instanceId"]!.ToString() == claims[0]["instanceId"]!.ToString(), "instance ID stays stable for the process");
}

var listCalls = 0; var claimCalls = 0;
using (var transport = new Fixture(async (request, token) =>
{
    if (request.Method == HttpMethod.Get)
    {
        if (request.RequestUri!.AbsolutePath == "/api/worker/accounts") listCalls++;
        return Fixture.AccountResponse(request);
    }
    claimCalls++; var body = await Fixture.Body(request);
    return claimCalls == 1 ? Fixture.Error("ACCOUNT_CLAIMED") : Fixture.Json(new { data = Fixture.Assignment(body) });
}))
using (var client = transport.Client())
{
    var receiver = Fixture.Receiver(client);
    Check(await receiver.ReceiveAsync(default) is null, "another worker winning a claim is normal");
    using var next = await receiver.ReceiveAsync(default);
    Check(listCalls == 2 && next is not null, "claim conflict refreshes the queue");
}
using (var transport = new Fixture((request, token) => Task.FromResult(request.Method == HttpMethod.Get
    ? Fixture.AccountResponse(request) : Fixture.Error("WORKER_INSTANCE_BUSY"))))
using (var client = transport.Client())
    await Throws<InvalidDataException>(async () => { using var unused = await Fixture.Receiver(client).ReceiveAsync(default); }, "unknown active ownership never starts a second account");
using (var transport = new Fixture((request, token) => Task.FromResult(Fixture.Error("UNAUTHORIZED", HttpStatusCode.Unauthorized))))
using (var client = transport.Client())
    await Throws<LotusClientException>(async () => { using var unused = await Fixture.Receiver(client).ReceiveAsync(default); }, "bad API key is not treated as an empty queue");
using (var transport = new Fixture((request, token) => Task.FromResult(Fixture.Json(new { data = Array.Empty<object>() }))))
using (var client = transport.Client())
    Check(await Fixture.Receiver(client).ReceiveAsync(default) is null, "empty queue waits without claiming");

var proxyAssignmentForClaim = Fixture.Assignment();
proxyAssignmentForClaim["proxy"] = new JsonObject
{
    ["id"] = "proxy-1",
    ["enabled"] = true,
    ["address"] = "http://proxy.invalid:90",
    ["username"] = "proxy-user",
    ["password"] = "proxy-secret-from-proxy-api",
    ["expiresAt"] = Fixture.ServerTime.AddHours(1)
};
var proxyApiCalls = new List<string>();
using (var transport = new Fixture(async (request, token) =>
{
    var path = request.RequestUri!.AbsolutePath;
    proxyApiCalls.Add(path);
    if (path == "/api/worker/accounts") return Fixture.AccountResponse(request);
    if (path == $"/api/accounts/{Fixture.AccountId}")
        return Fixture.Json(new { data = new { id = Fixture.AccountId, identityNumber = "000000000001", proxyId = "proxy-1" } });
    if (path == "/api/proxies/proxy-1")
        return Fixture.Json(new
        {
            data = new
            {
                id = "proxy-1",
                enabled = true,
                address = "http://proxy.invalid:90",
                username = "proxy-user",
                password = "proxy-secret-from-proxy-api",
                expiresAt = Fixture.ServerTime.AddHours(1)
            }
        });
    var claimBody = await Fixture.Body(request);
    var claimAssignment = Fixture.Assignment(claimBody);
    claimAssignment["proxy"] = proxyAssignmentForClaim["proxy"]?.DeepClone();
    return Fixture.Json(new { data = claimAssignment });
}))
using (var client = transport.Client())
{
    var accountProxyProvider = new AccountWebProxyProvider();
    using var context = await Fixture.Receiver(client, proxyProvider: accountProxyProvider).ReceiveAsync(default);
    var configuredProxy = accountProxyProvider.GetProxy();
    Check(configuredProxy.Credentials?.GetCredential(
            new Uri("http://proxy.invalid:90"), "Basic")?.Password == "proxy-secret-from-proxy-api",
        "LMS HTTP transport receives account proxy settings from the dedicated proxy API");
    var proxyIp = await new ProxyIpChecker(accountProxyProvider, () => new Fixture(
        (request, token) => Task.FromResult(Fixture.Json(new { ip = "203.0.113.42" }))))
        .GetProxyIpAsync(default);
    Check(proxyIp.ToString() == "203.0.113.42",
        "proxy IP check reports the egress IP returned through the account proxy");
    await Throws<ProxyIpCheckException>(
        () => new ProxyIpChecker(accountProxyProvider, () => new Fixture(
            (request, token) => Task.FromResult(Fixture.Json(new { ip = "not-an-ip" }))))
            .GetProxyIpAsync(default),
        "invalid proxy IP response fails instead of reporting success");
    Check(context?.CreateWebProxy()?.Credentials?.GetCredential(
            new Uri("http://proxy.invalid:90"), "Basic")?.Password == "proxy-secret-from-proxy-api",
        "account context retains proxy API credentials");
    Check(proxyApiCalls.SequenceEqual([
            "/api/worker/accounts",
            $"/api/worker/accounts/{Fixture.AccountId}/claim",
            $"/api/accounts/{Fixture.AccountId}",
            "/api/proxies/proxy-1"
        ]),
        "worker reads account details then fetches the assigned proxy after claim");
}

var clock = new ManualClock();
using (var context = Fixture.Context(clock: clock))
{
    Check(context.RemainingLease.TotalSeconds is > 29 and < 30, "lease derives from server duration despite different wall clocks");
    var first = context.CreateLmsOptions(); first.Tenant = "changed";
    Check(context.CreateLmsOptions().Tenant == "truongson" && context.CreateLmsOptions().WebOrigin == "https://truongson.invalid", "per-account options are fresh and retain provider context");
    Check(context.CreateLoginRequest().DeviceUuid == Fixture.AccountId, "account device GUID is reused");
    Check(context.CreateLoginRequest().Provider is { BaseUrl: "https://lms.invalid", WebOrigin: "https://truongson.invalid", Tenant: "truongson" },
        "login receives the claimed account provider instead of worker options");
    Check(context.CreateWebProxy() is null, "direct account uses no proxy");
    Check(!context.ToString()!.Contains("secret"), "context text does not expose secrets");
    clock.JumpWallClock(TimeSpan.FromDays(365));
    Check(context.RemainingLease.TotalSeconds > 29, "wall clock jumps cannot extend or revoke a lease");
    clock.Advance(TimeSpan.FromSeconds(10)); var started = clock.GetTimestamp();
    clock.Advance(TimeSpan.FromSeconds(2));
    var control = Fixture.Assignment(); control["serverTime"] = Fixture.ServerTime.AddSeconds(10); control["leaseExpiresAt"] = Fixture.ServerTime.AddSeconds(40);
    context.UpdateControl(Fixture.Read(control), started);
    Check(context.RemainingLease.TotalSeconds is > 27 and < 28, "heartbeat renewal subtracts request latency");
    clock.Advance(TimeSpan.FromSeconds(28));
    Check(context.StoppingToken.IsCancellationRequested && context.LeaseLost, "timer cancels an expired lease independently of network activity");
    context.UpdateControl(Fixture.Read(control), clock.GetTimestamp());
    await Throws<OperationCanceledException>(() => { context.CreateLmsOptions(); return Task.CompletedTask; }, "late heartbeat cannot revive an expired context");
}
var proxyAssignment = Fixture.Assignment(); proxyAssignment["proxy"] = new JsonObject
{ ["id"] = "proxy-1", ["enabled"] = true, ["address"] = "http://proxy.invalid:90", ["username"] = "proxy-user", ["password"] = "proxy-secret", ["expiresAt"] = Fixture.ServerTime.AddSeconds(3) };
var directProxyProvider = new AccountWebProxyProvider();
directProxyProvider.Apply(null, Fixture.ServerTime);
Check(directProxyProvider.GetProxy().IsBypassed(new Uri("https://lms.invalid")),
    "an account without an assigned proxy connects directly");
var proxyClock = new ManualClock();
using (var context = Fixture.Context(proxyAssignment, proxyClock))
{
    var proxy = context.CreateWebProxy()!;
    Check(proxy.GetProxy(new Uri("https://lms.invalid"))!.Host == "proxy.invalid", "proxy comes from the claimed account");
    Check(proxy.Credentials!.GetCredential(new Uri("http://proxy.invalid:90"), "Basic")!.Password == "proxy-secret", "proxy credentials are prepared without appsettings");
    proxyClock.Advance(TimeSpan.FromSeconds(3));
    Check(context.StoppingToken.IsCancellationRequested, "proxy expiry cancels before the longer account lease");
}
using (var context = Fixture.Context())
{
    var changed = Fixture.Assignment(); changed["enrolmentPlanIid"] = 100;
    context.UpdateControl(Fixture.Read(changed), TimeProvider.System.GetTimestamp());
    Check(context.StoppingToken.IsCancellationRequested && !context.LeaseLost, "changed programme stops the current context");
}
using (var context = Fixture.Context())
{
    var changed = Fixture.Assignment(); changed["proxy"] = new JsonObject { ["enabled"] = true, ["address"] = "http://other.invalid:90" };
    context.UpdateControl(Fixture.Read(changed), TimeProvider.System.GetTimestamp());
    Check(context.StoppingToken.IsCancellationRequested, "proxy change cannot silently reuse connections");
}
foreach (var field in new[] { "enrolmentPlanIid", "credentials", "lms" })
{
    var invalid = Fixture.Assignment(); invalid[field] = null;
    using var context = Fixture.Context(invalid);
    Check(context.ValidateConfiguration() is not null, "missing " + field + " must fail before LMS work");
}
var otherAssignment = Fixture.Assignment(); otherAssignment["provider"] = "yenthuy";
otherAssignment["lms"] = new JsonObject { ["base"] = "https://other-lms.invalid", ["tenant"] = "yenthuy", ["origin"] = "https://yenthuy.invalid" };
using (var first = Fixture.Context())
using (var second = Fixture.Context(otherAssignment))
    Check(first.CreateLmsOptions().Tenant == "truongson" && second.CreateLmsOptions().Tenant == "yenthuy", "consecutive accounts have isolated provider options");

var logEntries = new List<JsonObject>();
using (var transport = new Fixture(async (request, token) =>
{
    logEntries.Add(await Fixture.Body(request));
    return logEntries.Count == 1 ? Fixture.Error("TEMPORARY", HttpStatusCode.ServiceUnavailable) : Fixture.Json(new { data = new { id = 1, duplicate = true } });
}))
using (var client = transport.Client())
using (var context = Fixture.Context())
{
    var logs = new AccountLog(client, TimeProvider.System, NullLogger<AccountLog>.Instance);
    await logs.SendAsync(context, "info", "Đã tiếp nhận học viên.", default);
    Check(logEntries.Count == 2 && JsonNode.DeepEquals(logEntries[0], logEntries[1]), "log retry preserves event ID, ownership and timestamp");
    Check(logEntries[0]["context"]?["enrolmentPlanIid"]?.GetValue<int>() == 99, "worker log includes selected programme");
    Check(!logEntries[0].ToJsonString().Contains("fixture-student-secret"), "log never includes student credentials");
}

using (var transport = new Fixture((request, token) =>
{
    var control = Fixture.Assignment(); control["status"] = "stopped"; control["state"] = "stopping";
    return Task.FromResult(Fixture.Json(new { data = control }));
}))
using (var client = transport.Client())
using (var context = Fixture.Context())
{
    await new AccountLease(client, Fixture.Options, TimeProvider.System, NullLogger<AccountLease>.Instance).RunAsync(context, default).WaitAsync(TimeSpan.FromSeconds(4));
    Check(context.StoppingToken.IsCancellationRequested && !context.LeaseLost, "backend stop cancels work and permits finish acknowledgment");
}
var heartbeats = 0;
using (var transport = new Fixture((request, token) => { heartbeats++; return Task.FromResult(Fixture.Error("TEMPORARY", HttpStatusCode.ServiceUnavailable)); }))
using (var client = transport.Client())
{
    var assignment = Fixture.Assignment(); assignment["leaseExpiresAt"] = Fixture.ServerTime.AddSeconds(3);
    using var context = Fixture.Context(assignment);
    await new AccountLease(client, Fixture.Options, TimeProvider.System, NullLogger<AccountLease>.Instance).RunAsync(context, default).WaitAsync(TimeSpan.FromSeconds(5));
    Check(heartbeats >= 1 && context.LeaseLost, "network retries cannot retain an expired lease");
}
using (var transport = new Fixture((request, token) => Task.FromResult(Fixture.Error("LEASE_LOST"))))
using (var client = transport.Client())
using (var context = Fixture.Context())
{
    await new AccountLease(client, Fixture.Options, TimeProvider.System, NullLogger<AccountLease>.Instance).RunAsync(context, default).WaitAsync(TimeSpan.FromSeconds(4));
    Check(context.LeaseLost, "explicit lease loss immediately revokes context");
}
using (var transport = new Fixture(async (request, token) =>
{
    await Task.Delay(Timeout.InfiniteTimeSpan, token);
    throw new Exception("Unreachable");
}))
using (var client = transport.Client())
{
    var assignment = Fixture.Assignment(); assignment["leaseExpiresAt"] = Fixture.ServerTime.AddSeconds(3);
    using var context = Fixture.Context(assignment);
    await new AccountLease(client, Fixture.Options, TimeProvider.System, NullLogger<AccountLease>.Instance).RunAsync(context, default).WaitAsync(TimeSpan.FromSeconds(5));
    Check(context.LeaseLost, "a hanging request is canceled at the lease deadline");
}
using (var transport = new Fixture((request, token) => Task.FromResult(Fixture.Error("WORKER_UNAUTHORIZED", HttpStatusCode.Unauthorized))))
using (var client = transport.Client())
using (var context = Fixture.Context())
{
    await Throws<LotusClientException>(() => new AccountLease(client, Fixture.Options, TimeProvider.System,
        NullLogger<AccountLease>.Instance).RunAsync(context, default).WaitAsync(TimeSpan.FromSeconds(4)), "permanent heartbeat authentication failure reaches worker supervisor");
    Check(context.StoppingToken.IsCancellationRequested, "fatal heartbeat failures revoke the context before propagating");
}
Console.WriteLine($"PASS: {checks} Worker V2 checks; mocked backend, no LMS requests.");
