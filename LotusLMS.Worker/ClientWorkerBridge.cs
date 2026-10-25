using System.Net;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using LotusLMS.Api;
using LotusLMS.Client;

namespace LotusLMS.Worker;

internal sealed class ClientWorkerOptions : LotusClientOptions
{
    public bool Enabled { get; init; }
    public string AccountId { get; init; } = string.Empty;
    public string WorkerName { get; init; } = string.Empty;
}

/// <summary>Claims one queued student, maintains its lease and forwards logs for that run.</summary>
internal sealed class ClientWorkerBridge : BackgroundService, ILoggerProvider
{
    private readonly ClientWorkerOptions _options;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly IServiceProvider? _services;
    private readonly ClientAccountProxyProvider? _proxyProvider;
    private ILotusClient? _client;
    private readonly IDisposable? _ownedClient;
    private readonly string _instanceId = Guid.NewGuid().ToString();
    private readonly Channel<ClientWorkerLog> _queue = Channel.CreateBounded<ClientWorkerLog>(
        new BoundedChannelOptions(5000) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly TaskCompletionSource _permission = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _leaseLost = new();
    private Task _sender = Task.CompletedTask;
    private readonly TimeSpan _controlInterval;
    private ClientAssignment? _assignment;
    private long _deadline;
    private string _outcome = "stopped";
    private string? _pendingAccount;
    private string? _requestId;

    public ClientWorkerBridge(IOptions<ClientWorkerOptions> options, IServiceProvider services)
        : this(options.Value, null, (ILotusClient?)null)
    {
        _services = services;
    }

    internal ClientWorkerBridge(ClientWorkerOptions options, IHostApplicationLifetime? lifetime, HttpClient http,
        TimeSpan? controlInterval = null, ClientAccountProxyProvider? proxyProvider = null)
        : this(options, lifetime, new LotusClient(http, Options.Create<LotusClientOptions>(options)), controlInterval, proxyProvider)
    {
        _ownedClient = (IDisposable)_client!;
    }

    private ClientWorkerBridge(ClientWorkerOptions options, IHostApplicationLifetime? lifetime, ILotusClient? client,
        TimeSpan? controlInterval = null, ClientAccountProxyProvider? proxyProvider = null)
    {
        _options = options; _lifetime = lifetime; _proxyProvider = proxyProvider; _client = client;
        _controlInterval = controlInterval ?? TimeSpan.FromSeconds(5);
    }
    public LotusLmsCredentialsOptions? Credentials { get; private set; }
    public Task WaitForPermissionAsync(CancellationToken token) => _options.Enabled ? _permission.Task.WaitAsync(token) : Task.CompletedTask;
    public ILogger CreateLogger(string category) => new ForwardingLogger(this, category);
    public void SetOutcome(string outcome) => _outcome = outcome;
    internal CancellationToken LeaseLost => _leaseLost.Token;
    internal TimeSpan RemainingLease()
    {
        if (!_options.Enabled) return Timeout.InfiniteTimeSpan;
        var remaining = TimeSpan.FromSeconds((Interlocked.Read(ref _deadline) - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
        if (_assignment is null || _leaseLost.IsCancellationRequested || remaining <= TimeSpan.Zero)
            throw new InvalidOperationException("Worker has no active student lease; LMS request was blocked.");
        return remaining;
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if (!_options.Enabled) return;
        _sender = SendLogsAsync(token);
        try { await Task.WhenAll(_sender, MonitorAsync(token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private async Task MonitorAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_assignment is null) await TryClaimAsync(token);
                else
                {
                    var started = Stopwatch.GetTimestamp();
                    var control = await Client.HeartbeatAsync(_assignment.AccountId, Ownership(), token);
                    var provider = ProxyProvider;
                    if (control.Status != "running" || (provider is not null && !provider.Apply(control.Proxy)))
                    {
                        Revoke("Backend requested stop or changed account proxy; shutting down worker."); return;
                    }
                    UpdateDeadline(control, started);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
            {
                Console.Error.WriteLine("Worker queue/control is unavailable; no credentials or response body were logged.");
                if (_assignment is not null) { Revoke("Worker lease/control unavailable; shutting down worker."); return; }
            }
            await Task.Delay(_controlInterval, token);
        }
    }
    private async Task TryClaimAsync(CancellationToken token)
    {
        // Keep a claim request across network retries. The server returns the same run and token.
        if (_pendingAccount is null)
        {
            var result = await Client.GetAccountsAsync(new ClientAccountQuery { AccountId = _options.AccountId }, token);
            _pendingAccount = result.Data.FirstOrDefault()?.Id;
            if (_pendingAccount is null) return;
            _requestId = Guid.NewGuid().ToString();
        }
        var started = Stopwatch.GetTimestamp();
        ClientAssignment assignment;
        try
        {
            assignment = await Client.ClaimAsync(_pendingAccount, new ClientClaimRequest
            {
                WorkerName = _options.WorkerName, InstanceId = _instanceId, RequestId = _requestId!
            }, token);
        }
        catch (LotusClientException error) when (error.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            _pendingAccount = null; _requestId = null; return;
        }
        if (assignment.AccountId != _pendingAccount || assignment.InstanceId != _instanceId || assignment.WorkerName != _options.WorkerName
            || string.IsNullOrEmpty(assignment.RunId) || string.IsNullOrEmpty(assignment.LeaseToken) || assignment.Credentials is null)
            throw new InvalidDataException("Claim response is invalid.");
        _assignment = assignment;
        Credentials = new LotusLmsCredentialsOptions
        {
            Username = assignment.Credentials.Username, Password = assignment.Credentials.Password,
            DeviceUuid = assignment.Credentials.DeviceUuid
        };
        UpdateDeadline(assignment, started);
        if (assignment.Status != "running") throw new InvalidDataException("Claim is no longer runnable.");
        var account = await Client.GetAccountAsync(assignment.AccountId, token);
        if (account.Id != assignment.AccountId || account.ProxyId != assignment.Proxy?.Id)
            throw new InvalidDataException("Account details do not match the claimed proxy assignment.");
        var proxy = account.ProxyId is { Length: > 0 } proxyId
            ? await Client.GetProxyAsync(proxyId, assignment.AccountId, Ownership(), token)
            : null;
        if (proxy is not null && proxy.Id != account.ProxyId)
            throw new InvalidDataException("Proxy details do not match the account assignment.");
        ProxyProvider?.Apply(proxy);
        if (_services is not null)
        {
            if (assignment.Lms is null || assignment.Provider is not ("yenthuy" or "truongson")
                || !Uri.TryCreate(assignment.Lms.Base, UriKind.Absolute, out var apiUri) || apiUri.Scheme != "https"
                || !Uri.TryCreate(assignment.Lms.Origin, UriKind.Absolute, out var webUri) || webUri.Scheme != "https"
                || assignment.Lms.Tenant != assignment.Provider)
                throw new InvalidDataException("Claim LMS provider is invalid.");
            var options = _services.GetRequiredService<IOptions<LotusLmsOptions>>().Value;
            options.BaseUrl = assignment.Lms.Base; options.WebOrigin = assignment.Lms.Origin; options.Tenant = assignment.Lms.Tenant;
        }
        _queue.Writer.TryWrite(NewEvent("info", "Student account claimed; worker is ready.", "ClientQueue"));
        _permission.TrySetResult();
    }
    // Logging constructs this bridge before HttpClientFactory; resolve the transport when monitoring starts.
    private ILotusClient Client => _client ??= _services!.GetRequiredService<ILotusClient>();
    private ClientAccountProxyProvider? ProxyProvider => _proxyProvider ?? _services?.GetRequiredService<ClientAccountProxyProvider>();
    private void UpdateDeadline(ClientRunControl data, long started)
    {
        var duration = data.LeaseExpiresAt - data.ServerTime;
        if (duration <= TimeSpan.FromSeconds(1) || duration > TimeSpan.FromMinutes(1))
            throw new InvalidDataException("Invalid worker lease duration.");
        var deadline = started + (long)((duration.TotalSeconds - 1) * Stopwatch.Frequency);
        if (deadline <= Stopwatch.GetTimestamp()) throw new InvalidDataException("Worker lease response arrived too late.");
        Interlocked.Exchange(ref _deadline, deadline);
    }
    private void Revoke(string message)
    {
        Interlocked.Exchange(ref _deadline, 0); _leaseLost.Cancel();
        _queue.Writer.TryWrite(NewEvent("warn", message, "ClientControl"));
        Lifetime.StopApplication();
    }
    private IHostApplicationLifetime Lifetime => _lifetime ?? _services!.GetRequiredService<IHostApplicationLifetime>();
    private ClientRunOwnership Ownership() => new()
    {
        RunId = _assignment!.RunId, LeaseToken = _assignment.LeaseToken, InstanceId = _instanceId
    };
    private ClientWorkerLog NewEvent(string level, string message, string category) => new()
    {
        EventId = Guid.NewGuid().ToString(), AccountId = _assignment!.AccountId, WorkerName = _options.WorkerName,
        RunId = _assignment.RunId, LeaseToken = _assignment.LeaseToken, InstanceId = _instanceId,
        Level = level, Message = message.Length > 10000 ? message[..10000] : message, OccurredAt = DateTimeOffset.UtcNow,
        Context = new Dictionary<string, object?> { ["category"] = category }
    };
    private async Task SendLogsAsync(CancellationToken token)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(token))
        {
            var attempt = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Client.SendLogAsync(item, token);
                    break;
                }
                catch (LotusClientException error) when (error.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict) { break; }
                catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested) { }
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(++attempt, 5)), token);
            }
        }
    }
    public override async Task StopAsync(CancellationToken token)
    {
        _queue.Writer.TryComplete();
        await Task.WhenAny(_sender, Task.Delay(TimeSpan.FromSeconds(2), token));
        await base.StopAsync(token);
        _leaseLost.Cancel();
        if (_assignment is null) return;
        try
        {
            using var finishTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            finishTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            await Client.FinishAsync(_assignment.AccountId, new ClientFinishRequest
            {
                RunId = _assignment.RunId, LeaseToken = _assignment.LeaseToken, InstanceId = _instanceId, Outcome = _outcome
            }, finishTimeout.Token);
        }
        catch { Console.Error.WriteLine("Could not acknowledge worker completion; server lease will expire."); }
    }
    public override void Dispose() { _ownedClient?.Dispose(); _leaseLost.Dispose(); base.Dispose(); }

    private sealed class ForwardingLogger(ClientWorkerBridge bridge, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => bridge._options.Enabled && bridge._assignment is not null && level != LogLevel.None
            && category.StartsWith("LotusLMS.Worker", StringComparison.Ordinal);
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var mapped = level switch
            {
                LogLevel.Trace or LogLevel.Debug => "debug", LogLevel.Information => "info",
                LogLevel.Warning => "warn", _ => "error"
            };
            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message)) return;
            var item = bridge.NewEvent(mapped, message, category);
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
            {
                foreach (var field in fields)
                {
                    if (field.Key == "{OriginalFormat}") continue;
                    var sensitive = field.Key.Contains("password", StringComparison.OrdinalIgnoreCase)
                        || field.Key.Contains("token", StringComparison.OrdinalIgnoreCase)
                        || field.Key.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                        || field.Key.Contains("authorization", StringComparison.OrdinalIgnoreCase);
                    item.Context[System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(field.Key)] =
                        sensitive ? "[redacted]" : field.Value;
                }
            }
            bridge._queue.Writer.TryWrite(item);
        }
    }

}
