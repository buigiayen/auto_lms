using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LotusLMS.Worker.V2.Notifications;

internal sealed class DiscordWorkerNotifier : IWorkerNotifier, IDisposable
{
    private sealed class Destination(int number, Uri uri)
    {
        public int Number { get; } = number;
        public Uri Uri { get; } = uri;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool Disabled;
        public long NotBeforeTicks;
    }

    private readonly HttpClient _http;
    private readonly DiscordNotificationOptions _options;
    private readonly string _serverName;
    private readonly string _workerName;
    private readonly TimeProvider _clock;
    private readonly Action<string> _report;
    private readonly HashSet<string> _events = new(StringComparer.Ordinal);
    private readonly List<Destination> _destinations = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recent = new();
    private long _globalNotBeforeTicks;

    internal DiscordWorkerNotifier(HttpClient http, DiscordNotificationOptions options, string serverName,
        string workerName, TimeProvider clock, Action<string> report)
    {
        _http = http;
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _options = options;
        _serverName = serverName;
        _workerName = workerName;
        _clock = clock;
        _report = report;
        if (!options.Enabled) return;
        var errors = options.ValidationErrors().ToArray();
        foreach (var error in errors) report("[Discord] " + error);
        if (errors.Length > 0) return;
        foreach (var name in (options.Events ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (name is "worker_failed" or "lane_failed" or "account_failed") _events.Add(name);
            else report("[Discord] Discord:Events có loại sự kiện không hỗ trợ; bỏ qua mục đó.");
        }
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var value in (options.WebhookUrls ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            number++;
            if (!DiscordNotificationOptions.TryWebhook(value, out var uri))
            {
                report($"[Discord] Discord:WebhookUrls mục #{number} không hợp lệ; bỏ qua.");
                continue;
            }
            if (unique.Add(uri.AbsoluteUri)) _destinations.Add(new(number, uri));
        }
        if (_destinations.Count == 0) report("[Discord] Không có webhook hợp lệ; worker tiếp tục chạy và chỉ ghi log.");
    }

    internal static DiscordWorkerNotifier FromConfiguration(IConfiguration configuration)
    {
        var settings = new DiscordNotificationOptions();
        try { configuration.GetSection(DiscordNotificationOptions.SectionName).Bind(settings); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or FormatException)
        {
            Console.Error.WriteLine("[Discord] Không đọc được cấu hình Discord; vô hiệu hóa thông báo và tiếp tục chạy worker.");
            settings = new();
        }
        var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
        var server = configuration["Worker:ServerName"];
        return new(http, settings, string.IsNullOrWhiteSpace(server) ? Environment.MachineName : server.Trim(),
            configuration["Worker:WorkerName"] ?? "worker-v2", TimeProvider.System, Console.Error.WriteLine);
    }

    public async Task NotifyAsync(WorkerFailure failure, CancellationToken cancellationToken = default)
    {
        if (!_events.Contains(failure.EventType) || _destinations.Count == 0 || cancellationToken.IsCancellationRequested) return;
        try
        {
            var now = _clock.GetUtcNow();
            foreach (var previous in _recent)
                if (now - previous.Value > TimeSpan.FromMinutes(10)) _recent.TryRemove(previous.Key, out _);
            var key = JsonSerializer.Serialize(new[] { failure.EventType, failure.Code, failure.LaneName, failure.AccountId, failure.RunId });
            if (!_recent.TryAdd(key, now)) return;
            if (_recent.Count > 2048)
                foreach (var previous in _recent.OrderBy(entry => entry.Value).Take(_recent.Count - 2048))
                    _recent.TryRemove(previous.Key, out _);

            var eventId = Guid.NewGuid().ToString();
            var occurredAt = now.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            var record = new JsonObject
            {
                ["eventId"] = eventId, ["eventType"] = failure.EventType, ["code"] = failure.Code,
                ["occurredAt"] = now.ToString("O"), ["serverName"] = _serverName, ["workerName"] = _workerName,
                ["laneName"] = failure.LaneName, ["accountId"] = failure.AccountId, ["runId"] = failure.RunId,
                ["reason"] = failure.Reason, ["details"] = JsonSerializer.SerializeToNode(failure.Details)
            };
            var file = Encoding.UTF8.GetBytes(record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var text = $"🔴 {failure.EventType} · {failure.Code}\nServer: {Short(_serverName, 100)}\nWorker: {Short(_workerName, 100)}"
                + $"\nLane: {Short(failure.LaneName, 100)}\nAccount: {Short(failure.AccountId, 100)}\nRun: {Short(failure.RunId, 100)}"
                + $"\nThời gian: {occurredAt}\nLý do: {Short(failure.Reason, 800)}\nSự kiện: {eventId}\nChi tiết trong file JSON đính kèm.";
            var filename = $"worker-failure-{eventId}.json";
            var payload = new JsonObject
            {
                ["content"] = Short(text, 1900),
                ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() },
                ["attachments"] = new JsonArray(new JsonObject { ["id"] = 0, ["filename"] = filename })
            }.ToJsonString();
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(_options.DeliveryTimeoutSeconds));
            await Parallel.ForEachAsync(_destinations, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = budget.Token },
                async (destination, token) => await SendAsync(destination, eventId, payload, filename, file, token));
        }
        catch (OperationCanceledException)
        {
            _report("[Discord] Đã hết thời gian gửi thông báo; worker tiếp tục xử lý.");
        }
        catch (Exception error)
        {
            // Never log URLs, response bodies or exception messages containing webhook tokens.
            _report($"[Discord] Không gửi được thông báo ({error.GetType().Name}); worker tiếp tục xử lý.");
        }
    }

    private async Task SendAsync(Destination destination, string eventId, string payload, string filename, byte[] file, CancellationToken token)
    {
        var acquired = false;
        try
        {
            await destination.Gate.WaitAsync(token);
            acquired = true;
            if (destination.Disabled) return;
            for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                await WaitForRateLimitAsync(destination, token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
                try
                {
                    using var content = new MultipartFormDataContent();
                    content.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "payload_json");
                    var attachment = new ByteArrayContent(file);
                    attachment.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    content.Add(attachment, "files[0]", filename);
                    using var request = new HttpRequestMessage(HttpMethod.Post, destination.Uri) { Content = content };
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    UpdateBucket(response, destination);
                    if (response.IsSuccessStatusCode)
                    {
                        _report($"[Discord] Webhook #{destination.Number} đã nhận thông báo {eventId}.");
                        return;
                    }
                    if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        destination.Disabled = true;
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var (delay, global) = await RetryAfterAsync(response, timeout.Token);
                        SetNotBefore(ref destination.NotBeforeTicks, delay);
                        if (global) SetNotBefore(ref _globalNotBeforeTicks, delay);
                    }
                    else if ((int)response.StatusCode < 500 && response.StatusCode != HttpStatusCode.RequestTimeout)
                    {
                        _report($"[Discord] Webhook #{destination.Number} trả HTTP {(int)response.StatusCode}; không retry.");
                        return;
                    }
                    else if (attempt < _options.MaxRetries)
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), _clock, token);
                    if (attempt == _options.MaxRetries)
                        _report($"[Discord] Webhook #{destination.Number} chưa nhận được thông báo sau {attempt + 1} lần gửi (HTTP {(int)response.StatusCode}).");
                }
                catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                    if (attempt == _options.MaxRetries)
                        _report($"[Discord] Webhook #{destination.Number} chưa nhận được thông báo ({error.GetType().Name}).");
                    else await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), _clock, token);
                }
            }
        }
        catch (OperationCanceledException) { _report($"[Discord] Webhook #{destination.Number} hết thời gian gửi."); }
        catch (Exception error) { _report($"[Discord] Webhook #{destination.Number} gặp lỗi {error.GetType().Name}."); }
        finally { if (acquired) destination.Gate.Release(); }
    }

    private async Task WaitForRateLimitAsync(Destination destination, CancellationToken token)
    {
        while (true)
        {
            var until = Math.Max(Interlocked.Read(ref destination.NotBeforeTicks), Interlocked.Read(ref _globalNotBeforeTicks));
            var delay = TimeSpan.FromTicks(until - _clock.GetUtcNow().UtcTicks);
            if (delay <= TimeSpan.Zero) return;
            await Task.Delay(delay, _clock, token);
        }
    }

    private async Task<(TimeSpan Delay, bool Global)> RetryAfterAsync(HttpResponseMessage response, CancellationToken token)
    {
        var seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds
            ?? (response.Headers.RetryAfter?.Date - _clock.GetUtcNow())?.TotalSeconds ?? 0;
        var global = response.Headers.TryGetValues("X-RateLimit-Global", out var flags) && flags.Contains("true");
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);
            var buffer = new char[4096];
            var count = await reader.ReadBlockAsync(buffer.AsMemory(), token);
            var root = JsonNode.Parse(new string(buffer, 0, count)) as JsonObject;
            if (root?["retry_after"] is JsonValue value && value.TryGetValue<double>(out var retry) && double.IsFinite(retry))
                seconds = Math.Max(seconds, retry);
            if (root?["global"] is JsonValue flag && flag.TryGetValue<bool>(out var isGlobal)) global |= isGlobal;
        }
        catch (JsonException) { }
        return (TimeSpan.FromSeconds(double.IsFinite(seconds) && seconds > 0 ? Math.Min(seconds, 86400) : 1), global);
    }

    private void UpdateBucket(HttpResponseMessage response, Destination destination)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0"
            && response.Headers.TryGetValues("X-RateLimit-Reset-After", out var reset)
            && double.TryParse(reset.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds > 0)
            SetNotBefore(ref destination.NotBeforeTicks, TimeSpan.FromSeconds(Math.Min(seconds, 86400)));
    }

    private void SetNotBefore(ref long target, TimeSpan delay)
    {
        var ticks = (_clock.GetUtcNow() + delay).UtcTicks;
        long previous;
        do { previous = Interlocked.Read(ref target); if (previous >= ticks) return; }
        while (Interlocked.CompareExchange(ref target, ticks, previous) != previous);
    }

    private static string Short(string? value, int length)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        return value.Length <= length ? value : value[..length] + "…";
    }

    public void Dispose()
    {
        _http.Dispose();
        foreach (var destination in _destinations) destination.Gate.Dispose();
    }
}
