using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using LotusLMS.Worker;
using LotusLMS.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal static class ClientWorkerChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string description)
        {
            if (!value) throw new Exception("FAIL: " + description);
            checks++;
        }
        using var handler = new ClientHandler { LoseFirstClaimResponse = true };
        using var http = new HttpClient(handler);
        var lifetime = new Lifetime();
        var options = new ClientWorkerOptions
        {
            Enabled = true, BaseUrl = "http://client.invalid/", AccountId = Guid.NewGuid().ToString(),
            WorkerName = "worker-fixture", ApiKey = "fixture-api-key"
        };
        using var bridge = new ClientWorkerBridge(options, lifetime, http, TimeSpan.FromMilliseconds(20));
        using var lmsHandler = new FakeLmsHandler();
        using var lms = new HttpClient(new ClientLeaseHandler(bridge) { InnerHandler = lmsHandler });
        try { await lms.GetAsync("https://lms.invalid/"); Check(false, "unclaimed worker must not send LMS requests"); }
        catch (InvalidOperationException) { Check(lmsHandler.Count == 0, "unclaimed worker cannot reach the LMS handler"); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await bridge.StartAsync(timeout.Token);
        await handler.FirstControl.Task.WaitAsync(timeout.Token);
        var permission = bridge.WaitForPermissionAsync(timeout.Token);
        Check(!permission.IsCompleted, "worker waits while account is stopped");
        handler.State = "running";
        await permission;
        Check(!lifetime.Stopped, "running permits work without stopping the host");
        Check(bridge.Credentials?.Username == "000000000001" && bridge.Credentials.Password == "fixture-password", "worker uses the claimed student credentials");
        Check(bridge.RemainingLease() > TimeSpan.Zero, "claim grants a bounded local lease");
        using var lmsResponse = await lms.GetAsync("https://lms.invalid/");
        Check(lmsHandler.Count == 1, "only a claimed worker can reach the LMS handler");
        bridge.CreateLogger("LotusLMS.Worker.LotusLmsPractice").LogInformation(
            "Practice {PracticeIid}: submitted {Submitted}/{Total} questions.", 11, 5, 58);
        await handler.FirstLog.Task.WaitAsync(timeout.Token);
        var log = handler.Logs.First(item => item["message"]!.GetValue<string>().Contains("submitted"));
        Check(log["accountId"]!.GetValue<string>() == options.AccountId, "event uses configured account ID");
        Check(log["workerName"]!.GetValue<string>() == options.WorkerName, "event uses configured worker name");
        Check(log["level"]!.GetValue<string>() == "info", "ILogger levels map to client levels");
        Check(Guid.TryParse(log["eventId"]!.GetValue<string>(), out _), "events get an idempotency UUID");
        Check(log["context"]?["practiceIid"]?.GetValue<int>() == 11, "structured context is preserved");
        Check(handler.Authorized, "both log and control requests use the API key");
        handler.State = "stopped";
        await lifetime.Stop.Task.WaitAsync(timeout.Token);
        Check(lifetime.Stopped, "stopped after running requests host shutdown");
        try { bridge.RemainingLease(); Check(false, "revoked lease must block LMS"); }
        catch (InvalidOperationException) { Check(true, "revoked lease blocks future LMS requests"); }
        try { await lms.GetAsync("https://lms.invalid/"); Check(false, "revoked request must fail"); }
        catch (InvalidOperationException) { Check(lmsHandler.Count == 1, "revocation prevents another LMS request"); }
        await bridge.StopAsync(timeout.Token);
        Check(handler.Finished, "worker acknowledges completion after stopping");
        Check(handler.Claims.All(item => item["requestId"]!.GetValue<string>() == handler.Claims.First()["requestId"]!.GetValue<string>()), "claim request ID remains stable across a lost response");

        using var disabledHttp = new HttpClient(handler);
        using var disabled = new ClientWorkerBridge(new ClientWorkerOptions(), new Lifetime(), disabledHttp);
        await disabled.WaitForPermissionAsync(timeout.Token);
        Check(!disabled.CreateLogger("LotusLMS.Worker.Worker").IsEnabled(LogLevel.Information),
            "disabled bridge does not forward or block existing worker behavior");

        var provider = new ClientAccountProxyProvider(true);
        var sharedProxy = provider.GetProxy()!;
        var destination = new Uri("https://lms.example/");
        var config = new ClientProxy { Id = "fixture", Enabled = true, Address = "http://proxy.example:29087",
            Username = "proxy-user", Password = "proxy-secret" };
        Check(provider.Apply(config), "initial account proxy can be applied after handler creation");
        Check(ReferenceEquals(sharedProxy, provider.GetProxy()) && sharedProxy.GetProxy(destination) == new Uri(config.Address),
            "pre-created HTTP handler sees the selected account proxy");
        Check(sharedProxy.Credentials?.GetCredential(destination, "Basic")?.Password == "proxy-secret",
            "proxy credentials are applied with the selected address");
        Check(provider.Apply(config), "unchanged configuration keeps the worker running");
        Check(!provider.Apply(null) && sharedProxy.GetProxy(destination) == new Uri(config.Address),
            "a running proxy cannot silently switch to direct connections");
        var directProvider = new ClientAccountProxyProvider(true);
        Check(directProvider.Apply(null) && directProvider.GetProxy()!.IsBypassed(destination),
            "unassigned account explicitly connects directly");
        try { new ClientAccountProxyProvider(true).Apply(new ClientProxy { Enabled = false });
            Check(false, "disabled profile must fail"); }
        catch (InvalidDataException) { Check(true, "disabled profile cannot grant permission"); }
        try { new ClientAccountProxyProvider(true).Apply(new ClientProxy
            { Enabled = true, Address = "http://proxy.example:29087", ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) });
            Check(false, "expired profile must fail"); }
        catch (InvalidDataException) { Check(true, "expired profile cannot grant permission even if enabled"); }
        Check(new ClientAccountProxyProvider(true).Apply(new ClientProxy
            { Enabled = true, Address = "http://proxy.example:29087", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) }),
            "unexpired profile permits work");

        using var changeHandler = new ClientHandler { State = "running", Proxy = System.Text.Json.JsonSerializer.Serialize(config) };
        using var changeHttp = new HttpClient(changeHandler);
        var changeLifetime = new Lifetime();
        using var changeBridge = new ClientWorkerBridge(options, changeLifetime, changeHttp,
            TimeSpan.FromMilliseconds(20), new ClientAccountProxyProvider(true));
        await changeBridge.StartAsync(timeout.Token);
        await changeBridge.WaitForPermissionAsync(timeout.Token);
        changeHandler.Proxy = "null";
        await changeLifetime.Stop.Task.WaitAsync(timeout.Token);
        Check(changeLifetime.Stopped, "changing selected proxy requests worker shutdown before more work");
        await changeBridge.StopAsync(timeout.Token);
        return checks;
    }

    private sealed class ClientHandler : HttpMessageHandler
    {
        public volatile string State = "stopped";
        public volatile string Proxy = "null";
        public bool LoseFirstClaimResponse;
        public bool Finished;
        private string _accountId = "";
        private string _instanceId = "";
        private readonly string _runId = Guid.NewGuid().ToString();
        public ConcurrentQueue<JsonObject> Claims { get; } = new();
        public bool Authorized { get; private set; } = true;
        public TaskCompletionSource FirstControl { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstLog { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<JsonObject> Logs { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorized &= request.Headers.Authorization?.ToString() == "Bearer fixture-api-key";
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                FirstControl.TrySetResult();
                if (path == "/api/worker/accounts")
                {
                    var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                    _accountId = query["accountId"] ?? "fixture-account";
                    return Json("{\"data\":" + (State == "running" ? "[{\"id\":\"" + _accountId + "\"}]" : "[]") + "}");
                }
                var profile = JsonNode.Parse(Proxy);
                if (path.StartsWith("/api/accounts/", StringComparison.Ordinal))
                {
                    var proxyId = (profile?["id"] ?? profile?["Id"])?.GetValue<string>();
                    return Json(new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["id"] = _accountId,
                            ["identityNumber"] = "000000000001",
                            ["proxyId"] = proxyId
                        }
                    }.ToJsonString());
                }
                if (path.StartsWith("/api/proxies/", StringComparison.Ordinal))
                    return Json(new JsonObject { ["data"] = profile }.ToJsonString());
                throw new InvalidOperationException($"Unexpected client API GET: {path}");
            }
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            if (path.EndsWith("/claim"))
            {
                Claims.Enqueue(body);
                _instanceId = body["instanceId"]!.GetValue<string>();
                if (LoseFirstClaimResponse) { LoseFirstClaimResponse = false; throw new HttpRequestException("Fixture lost claim response"); }
                return Control(true);
            }
            if (path.EndsWith("/heartbeat")) return Control(false);
            if (path.EndsWith("/finish")) { Finished = true; return Json(new JsonObject { ["data"] = new JsonObject { ["runId"] = _runId, ["state"] = body["outcome"]!.GetValue<string>() } }.ToJsonString()); }
            Logs.Enqueue(body);
            if (body["message"]!.GetValue<string>().Contains("submitted")) FirstLog.TrySetResult();
            return Json(new JsonObject { ["data"] = new JsonObject { ["id"] = 1, ["eventId"] = body["eventId"]!.GetValue<string>(), ["duplicate"] = false } }.ToJsonString());
        }
        private HttpResponseMessage Control(bool credentials)
        {
            var now = DateTimeOffset.UtcNow;
            var data = new JsonObject { ["accountId"] = _accountId, ["workerName"] = "worker-fixture", ["instanceId"] = _instanceId,
                ["runId"] = _runId, ["leaseToken"] = "fixture-lease", ["status"] = State,
                ["serverTime"] = now, ["leaseExpiresAt"] = now.AddSeconds(30), ["proxy"] = JsonNode.Parse(Proxy) };
            if (credentials) data["credentials"] = new JsonObject { ["username"] = "000000000001", ["password"] = "fixture-password", ["deviceUuid"] = Guid.NewGuid().ToString() };
            return Json(new JsonObject { ["data"] = data }.ToJsonString());
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public bool Stopped { get; private set; }
        public TaskCompletionSource Stop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ApplicationStarted => default;
        public CancellationToken ApplicationStopping => default;
        public CancellationToken ApplicationStopped => default;
        public void StopApplication() { Stopped = true; Stop.TrySetResult(); }
    }

    private sealed class FakeLmsHandler : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
