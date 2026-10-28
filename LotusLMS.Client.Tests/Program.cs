using System.Net;
using System.Text.Json.Nodes;
using LotusLMS.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    checks++;
}
using var handler = new FixtureHandler();
using var client = new LotusClient(new HttpClient(handler), Options.Create(new LotusClientOptions
    { BaseUrl = "http://client.invalid/root", ApiKey = "fixture-key" }));
var accountId = Guid.NewGuid().ToString();
var instanceId = Guid.NewGuid().ToString();
var runId = Guid.NewGuid().ToString();
var requestId = Guid.NewGuid().ToString();
const string leaseToken = "fixture-lease-secret";
var owner = new ClientRunOwnership { RunId = runId, InstanceId = instanceId, LeaseToken = leaseToken };
handler.Body = $$$"""{"data":[{"id":"{{{accountId}}}","identityNumber":"000000000001","managingTeacher":"Nguyễn Văn A","enrolmentPlanIid":99,"enrolmentPlanName":"Chương trình A","execution":{"state":"queued"}}],"pagination":{"page":2,"pageSize":10,"total":21}}""";
var page = await client.GetAccountsAsync(new ClientAccountQuery { AccountId = accountId, Page = 2, PageSize = 10 });
Check(page.Data.Single().EnrolmentPlanIid == 99 && page.Data.Single().EnrolmentPlanName == "Chương trình A", "queued account exposes selected enrolment plan");
Check(page.Data.Single().ManagingTeacher == "Nguyễn Văn A" && page.Pagination.Total == 21, "account data, teacher and paging deserialize");
Check(handler.Uri!.AbsolutePath == "/root/api/worker/accounts" && handler.Uri.Query.Contains("page=2") && handler.Uri.Query.Contains("pageSize=10"), "configured base path and pagination are retained");
Check(handler.Uri.Query.Contains("accountId=" + accountId) && handler.Authorization == "Bearer fixture-key", "account filter and authentication use the contract");
handler.Body = $$$"""{"data":{"id":"{{{accountId}}}","identityNumber":"000000000001","proxyId":"proxy-fixture"}}""";
var accountDetails = await client.GetAccountAsync(accountId);
Check(accountDetails.ProxyId == "proxy-fixture" && handler.Uri!.AbsolutePath == "/root/api/accounts/" + accountId,
    "account details use the account API endpoint to resolve the selected proxy");
var control = new JsonObject { ["accountId"] = accountId, ["workerName"] = "fixture-worker", ["runId"] = runId,
    ["status"] = "running", ["state"] = "accepted", ["enrolmentPlanIid"] = 99, ["enrolmentPlanName"] = "Chương trình A", ["serverTime"] = "2040-01-01T00:00:00Z", ["leaseExpiresAt"] = "2040-01-01T00:00:30Z",
    ["proxy"] = new JsonObject { ["enabled"] = true, ["address"] = "http://proxy.invalid:90", ["username"] = "proxy-user", ["password"] = "proxy-secret" } };
var assignment = (JsonObject)control.DeepClone();
assignment["instanceId"] = instanceId; assignment["leaseToken"] = leaseToken; assignment["provider"] = "truongson";
assignment["credentials"] = new JsonObject { ["username"] = "000000000001", ["password"] = "student-secret", ["deviceUuid"] = accountId };
assignment["lms"] = new JsonObject { ["tenant"] = "truongson", ["origin"] = "https://truongson.huelms.com", ["base"] = "https://staging-api.lotuslms.com" };
handler.Body = new JsonObject { ["data"] = assignment }.ToJsonString();
var claim = new ClientClaimRequest { WorkerName = "fixture-worker", InstanceId = instanceId, RequestId = requestId };
var received = await client.ClaimAsync(accountId, claim);
Check(handler.Method == HttpMethod.Post && handler.Uri!.AbsolutePath.EndsWith("/claim"), "claim uses its own endpoint");
Check(handler.Json?["workerName"]?.GetValue<string>() == claim.WorkerName && handler.Json?["requestId"]?.GetValue<string>() == requestId, "claim serializes named fields in camelCase");
Check(received.EnrolmentPlanIid == 99 && received.EnrolmentPlanName == "Chương trình A", "assignment DTO inherits selected enrolment plan fields");
Check(received.Credentials?.Password == "student-secret" && received.Proxy?.Password == "proxy-secret" && received.Lms?.Tenant == "truongson", "claim exposes reusable SDK DTOs for student, proxy and provider");
await client.ClaimAsync(accountId, claim);
Check(handler.Json?["requestId"]?.GetValue<string>() == requestId, "SDK preserves caller's idempotency ID on retries");
Check(!received.ToString()!.Contains(leaseToken) && !received.Credentials!.ToString()!.Contains("student-secret"), "default DTO text does not disclose secrets");
handler.Body = new JsonObject { ["data"] = control }.ToJsonString();
var beat = await client.HeartbeatAsync(accountId, owner);
Check(handler.Uri!.AbsolutePath.EndsWith("/heartbeat") && handler.Json?["leaseToken"]?.GetValue<string>() == leaseToken, "heartbeat sends ownership in JSON");
Check(beat.EnrolmentPlanIid == 99 && beat.EnrolmentPlanName == "Chương trình A", "heartbeat includes selected enrolment plan fields");
Check(beat.LeaseExpiresAt - beat.ServerTime == TimeSpan.FromSeconds(30), "lease timestamps deserialize without timezone loss");
await client.GetControlAsync(accountId, owner);
Check(handler.Method == HttpMethod.Get && handler.BodyReceived is null && handler.RunHeader == runId && handler.LeaseHeader == leaseToken && handler.InstanceHeader == instanceId, "read-only control sends ownership in headers");
Check(!handler.Uri!.ToString().Contains(leaseToken), "lease token never appears in a query string");
handler.Body = """{"data":{"id":"proxy-fixture","enabled":true,"address":"http://proxy.invalid:90","username":"proxy-user","password":"proxy-secret"}}""";
var proxy = await client.GetProxyAsync("proxy-fixture", accountId, owner);
Check(proxy.Password == "proxy-secret" && handler.Uri!.AbsolutePath == "/root/api/proxies/proxy-fixture"
    && handler.WorkerAccountHeader == accountId && handler.RunHeader == runId
    && handler.InstanceHeader == instanceId && handler.LeaseHeader == leaseToken,
    "proxy details use the proxy API with account ownership headers");
var eventId = Guid.NewGuid().ToString();
handler.Body = $$$"""{"data":{"id":12,"eventId":"{{{eventId}}}","duplicate":true}}""";
var log = new ClientWorkerLog { RunId = runId, InstanceId = instanceId, LeaseToken = leaseToken, EventId = eventId,
    AccountId = accountId, WorkerName = "fixture-worker", Level = "info", Message = "Đang học", Context = new() { ["courseIid"] = 22 } };
var receipt = await client.SendLogAsync(log);
Check(receipt.Duplicate && receipt.Id == 12 && handler.Json?["eventId"]?.GetValue<string>() == eventId, "log preserves eventId and returns deduplication receipt");
Check(handler.Json?["context"]?["courseIid"]?.GetValue<int>() == 22 && handler.Json?["runId"]?.GetValue<string>() == runId, "log contains structured context and run ownership");
handler.Body = $$$"""{"data":{"runId":"{{{runId}}}","state":"completed"}}""";
var finished = await client.FinishAsync(accountId, new ClientFinishRequest { RunId = runId, InstanceId = instanceId, LeaseToken = leaseToken, Outcome = "completed" });
Check(finished.State == "completed" && handler.Json?["outcome"]?.GetValue<string>() == "completed", "finish returns the typed result");
handler.Body = $$$"""{"data":{"runId":"{{{runId}}}","state":"deferred"}}""";
finished = await client.FinishAsync(accountId, new ClientFinishRequest { RunId = runId, InstanceId = instanceId, LeaseToken = leaseToken, Outcome = "deferred" });
Check(finished.State == "deferred" && handler.Json?["outcome"]?.GetValue<string>() == "deferred", "finish accepts daily-KPI deferral");
handler.Body = "{\"data\":{\"workerName\":\"new-worker\",\"apiKey\":\"new-key\"}}";
var registration = await client.RegisterWorkerAsync("new-worker");
Check(registration.ApiKey == "new-key" && handler.Uri!.AbsolutePath == "/root/api/workers", "worker registration is reusable outside the Worker app");

async Task ExpectFailure(string code, HttpStatusCode? status)
{
    try { await client.HeartbeatAsync(accountId, owner); Check(false, "failure must throw"); }
    catch (LotusClientException error)
    {
        Check(error.Code == code && error.StatusCode == status, "failure exposes status and symbolic code");
        Check(!error.ToString().Contains("student-secret") && !error.ToString().Contains("fixture-key"), "failure text omits server bodies and credentials");
    }
}
handler.Status = HttpStatusCode.Conflict; handler.Body = "{\"error\":{\"code\":\"ACCOUNT_CLAIMED\",\"message\":\"student-secret fixture-key\"}}";
var calls = handler.Count;
await ExpectFailure("ACCOUNT_CLAIMED", HttpStatusCode.Conflict);
Check(handler.Count == calls + 1, "SDK does not automatically retry a claim/control mutation");
handler.Status = HttpStatusCode.BadGateway; handler.Body = "<html>student-secret</html>";
await ExpectFailure("HTTP_ERROR", HttpStatusCode.BadGateway);
handler.Status = HttpStatusCode.BadRequest; handler.Body = "{\"error\":{\"code\":\"student-secret fixture-key\"}}";
await ExpectFailure("HTTP_ERROR", HttpStatusCode.BadRequest);
handler.Status = HttpStatusCode.OK; handler.Body = "{}";
await ExpectFailure("INVALID_RESPONSE", null);
handler.Body = "broken student-secret";
await ExpectFailure("INVALID_RESPONSE", HttpStatusCode.OK);
handler.Throw = new HttpRequestException("student-secret");
await ExpectFailure("CLIENT_CONNECTION_FAILED", null);
handler.Throw = new TaskCanceledException("fixture timeout");
await ExpectFailure("CLIENT_TIMEOUT", null);
handler.Throw = null; handler.Body = "{}";
using var canceled = new CancellationTokenSource(); canceled.Cancel();
try { await client.HeartbeatAsync(accountId, owner, canceled.Token); Check(false, "caller cancellation must propagate"); }
catch (OperationCanceledException) { Check(true, "caller cancellation is not converted into timeout"); }

using var emptyKeyHandler = new FixtureHandler { Body = "{\"data\":{\"workerName\":\"new\",\"apiKey\":\"new-key\"}}" };
using var registrationClient = new LotusClient(new HttpClient(emptyKeyHandler), Options.Create(new LotusClientOptions()));
await registrationClient.RegisterWorkerAsync("new");
Check(emptyKeyHandler.Authorization is null, "local worker registration does not require a pre-existing key");
foreach (var url in new[] { "ftp://backend.invalid", "https://name:student-secret@backend.invalid", "https://backend.invalid?key=fixture-key" })
{
    using var invalidHttp = new HttpClient(new FixtureHandler());
    try { using var invalid = new LotusClient(invalidHttp, Options.Create(new LotusClientOptions { BaseUrl = url })); Check(false, "unsafe backend URL must fail"); }
    catch (ArgumentException error) { Check(!error.Message.Contains("student-secret"), "invalid connection options fail without logging their values"); }
}
var services = new ServiceCollection();
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    { ["Client:BaseUrl"] = "http://di.invalid", ["Client:ApiKey"] = "di-key" }).Build();
services.AddLotusClient(config.GetSection("Client"));
var diHandler = new FixtureHandler { Body = "{\"data\":[],\"pagination\":{\"page\":1,\"pageSize\":20,\"total\":0}}" };
services.AddHttpClient<ILotusClient, LotusClient>().ConfigurePrimaryHttpMessageHandler(() => diHandler);
using var provider = services.BuildServiceProvider();
await provider.GetRequiredService<ILotusClient>().GetAccountsAsync();
Check(diHandler.Uri!.Host == "di.invalid" && diHandler.Authorization == "Bearer di-key", "DI binds settings and resolves the typed interface");
Check(!typeof(LotusClient).Assembly.GetReferencedAssemblies().Any(a => a.Name is "LotusLMS.Worker" or "LotusLMS.Api"), "SDK has no Worker or LMS API dependency");
Console.WriteLine($"PASS: {checks} standalone client SDK checks; mocked transport only.");

internal sealed class FixtureHandler : HttpMessageHandler
{
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Body = "{}";
    public Exception? Throw;
    public int Count;
    public Uri? Uri;
    public HttpMethod? Method;
    public string? Authorization, RunHeader, InstanceHeader, LeaseHeader, WorkerAccountHeader, BodyReceived;
    public JsonNode? Json;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Count++; token.ThrowIfCancellationRequested();
        if (Throw is not null) throw Throw;
        Uri = request.RequestUri; Method = request.Method; Authorization = request.Headers.Authorization?.ToString();
        RunHeader = Header("X-Worker-Run-Id"); InstanceHeader = Header("X-Worker-Instance-Id"); LeaseHeader = Header("X-Worker-Lease-Token");
        WorkerAccountHeader = Header("X-Worker-Account-Id");
        BodyReceived = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
        Json = BodyReceived is null ? null : JsonNode.Parse(BodyReceived);
        return new(Status) { Content = new StringContent(Body) };
        string? Header(string key) => request.Headers.TryGetValues(key, out var values) ? values.Single() : null;
    }
}
