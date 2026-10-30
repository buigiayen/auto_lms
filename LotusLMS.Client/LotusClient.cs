using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LotusLMS.Client;

/// <summary>Owns its supplied HttpClient; dispose the client when using it outside DI.</summary>
public sealed class LotusClient : ILotusClient, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public LotusClient(HttpClient http, IOptions<LotusClientOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Value.IsValid()) throw new ArgumentException("Client requires an HTTP(S) BaseUrl without credentials/query/fragment and a timeout from 1 to 300 seconds.", nameof(options));
        _http = http; _apiKey = options.Value.ApiKey;
        _http.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _http.Timeout = TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds);
    }
    public Task<ClientAccountPage> GetAccountsAsync(ClientAccountQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new();
        if (query.Page is < 1 or > 1_000_000 || query.PageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(query));
        var path = $"api/worker/accounts?page={query.Page}&pageSize={query.PageSize}";
        if (!string.IsNullOrEmpty(query.AccountId)) path += "&accountId=" + Uri.EscapeDataString(query.AccountId);
        return SendAsync<ClientAccountPage>(HttpMethod.Get, path, null, cancellationToken);
    }
    public Task<ClientAccount> GetAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return DataAsync<ClientAccount>(HttpMethod.Get, $"api/accounts/{Uri.EscapeDataString(accountId)}", null, cancellationToken);
    }
    public Task<ClientProxy> GetProxyAsync(
        string proxyId,
        string accountId,
        ClientRunOwnership ownership,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(ownership);
        return DataAsync<ClientProxy>(
            HttpMethod.Get,
            $"api/proxies/{Uri.EscapeDataString(proxyId)}",
            null,
            cancellationToken,
            ownership,
            accountId);
    }
    public Task<ClientAssignment> ClaimAsync(string accountId, ClientClaimRequest request, CancellationToken cancellationToken = default) =>
        DataAsync<ClientAssignment>(HttpMethod.Post, AccountPath(accountId, "claim"), request, cancellationToken);
    public Task<ClientRunControl> HeartbeatAsync(string accountId, ClientRunOwnership ownership, CancellationToken cancellationToken = default) =>
        DataAsync<ClientRunControl>(HttpMethod.Post, AccountPath(accountId, "heartbeat"), ownership, cancellationToken);
    public Task<ClientRunControl> GetControlAsync(string accountId, ClientRunOwnership ownership, CancellationToken cancellationToken = default) =>
        DataAsync<ClientRunControl>(HttpMethod.Get, AccountPath(accountId, "control"), null, cancellationToken, ownership);
    public Task<ClientFinishResult> FinishAsync(string accountId, ClientFinishRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Outcome is not ("completed" or "failed" or "stopped" or "deferred" or "resting")) throw new ArgumentException("Invalid worker outcome.", nameof(request));
        if (request.Outcome == "resting" ? request.RestSeconds is null or < 60 or > 86400 : request.RestSeconds is not null)
            throw new ArgumentException("RestSeconds is required from 60 to 86400 only for a resting outcome.", nameof(request));
        return DataAsync<ClientFinishResult>(HttpMethod.Post, AccountPath(accountId, "finish"), request, cancellationToken);
    }
    public Task<ClientLogReceipt> SendLogAsync(ClientWorkerLog log, CancellationToken cancellationToken = default) =>
        DataAsync<ClientLogReceipt>(HttpMethod.Post, "api/worker/logs", log, cancellationToken);
    public Task<ClientWorkerRegistration> RegisterWorkerAsync(string workerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerName);
        return DataAsync<ClientWorkerRegistration>(HttpMethod.Post, "api/workers", new { workerName }, cancellationToken);
    }
    private static string AccountPath(string id, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return $"api/worker/accounts/{Uri.EscapeDataString(id)}/{action}";
    }
    private async Task<T> DataAsync<T>(HttpMethod method, string path, object? body, CancellationToken token,
        ClientRunOwnership? ownership = null, string? workerAccountId = null) where T : class =>
        (await SendAsync<Envelope<T>>(method, path, body, token, ownership, workerAccountId)).Data
        ?? throw new LotusClientException(path, "INVALID_RESPONSE");
    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token,
        ClientRunOwnership? ownership = null, string? workerAccountId = null) where T : class
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(_apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (workerAccountId is not null) request.Headers.Add("X-Worker-Account-Id", workerAccountId);
        if (ownership is not null)
        {
            request.Headers.Add("X-Worker-Run-Id", ownership.RunId);
            request.Headers.Add("X-Worker-Instance-Id", ownership.InstanceId);
            request.Headers.Add("X-Worker-Lease-Token", ownership.LeaseToken);
        }
        try
        {
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                string? code = null;
                try
                {
                    var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(Json, token);
                    // Only the protocol's symbolic code is safe for logs; never include arbitrary server text.
                    if (error?.Error?.Code is { Length: > 0 and <= 80 } candidate && candidate.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')) code = candidate;
                }
                catch (JsonException) { }
                throw new LotusClientException(path, code ?? "HTTP_ERROR", response.StatusCode);
            }
            try { return await response.Content.ReadFromJsonAsync<T>(Json, token) ?? throw new LotusClientException(path, "INVALID_RESPONSE", response.StatusCode); }
            catch (JsonException) { throw new LotusClientException(path, "INVALID_RESPONSE", response.StatusCode); }
        }
        catch (HttpRequestException) { throw new LotusClientException(path, "CLIENT_CONNECTION_FAILED"); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new LotusClientException(path, "CLIENT_TIMEOUT"); }
    }
    public void Dispose() => _http.Dispose();
    private sealed class Envelope<T> where T : class { public T? Data { get; init; } }
    private sealed class ErrorEnvelope { public ErrorData? Error { get; init; } }
    private sealed class ErrorData { public string? Code { get; init; } }
}
