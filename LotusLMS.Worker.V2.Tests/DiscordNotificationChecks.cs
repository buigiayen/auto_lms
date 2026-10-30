using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using LotusLMS.Worker.V2.Notifications;

internal static class DiscordNotificationChecks
{
    private const string First = "https://discord.com/api/webhooks/111/fixture-hook-one";
    private const string Second = "https://discord.com/api/webhooks/222/fixture-hook-two";
    private static readonly WorkerFailure Failure = new("lane_failed", "RECEIVE_FAILED", "Lane không thể nhận account.",
        "server-worker-01", "account-1", "run-1");

    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var url in new[] { "http://discord.com/api/webhooks/1/token", "https://other.invalid/api/webhooks/1/token",
            "https://discord.com.evil.invalid/api/webhooks/1/token", "https://user:pass@discord.com/api/webhooks/1/token",
            "https://discord.com/api/webhooks/1/token/slack", First + "#fragment" })
            check(!DiscordNotificationOptions.TryWebhook(url, out _), "Discord rejects non-webhook or credential-bearing destinations");
        check(DiscordNotificationOptions.TryWebhook(First + "?wait=false&thread_id=123", out var threaded)
            && threaded.Query.Contains("wait=true") && !threaded.Query.Contains("wait=false") && threaded.Query.Contains("thread_id=123"),
            "Discord webhook validation enforces confirmation and preserves the thread destination");

        var calls = 0;
        using (var handler = new Fixture((_, _) => { calls++; throw new Exception("Disabled sender must not use HTTP"); }))
        using (var notifier = Make(handler, new DiscordNotificationOptions(), new()))
        {
            await notifier.NotifyAsync(Failure);
            check(calls == 0, "Discord is disabled by default without affecting worker execution");
        }

        var reports = new ConcurrentQueue<string>();
        var seen = new ConcurrentQueue<(string Path, JsonObject Payload, JsonObject Record)>();
        var attempts = new ConcurrentDictionary<string, int>();
        using (var handler = new Fixture(async (request, _) =>
        {
            var (payload, record) = await ReadAsync(request);
            var path = request.RequestUri!.AbsolutePath;
            seen.Enqueue((path, payload, record));
            lock (seen)
                check(request.Method == HttpMethod.Post && request.RequestUri.Query.Contains("wait=true"),
                    "every Discord delivery uses POST and waits for server confirmation");
            var attempt = attempts.AddOrUpdate(path, 1, (_, old) => old + 1);
            if (attempt == 1 && path.Contains("/111/")) return Fixture.Json(new { message = "temporary" }, HttpStatusCode.BadGateway);
            if (attempt == 1 && path.Contains("/222/"))
                return Fixture.Json(new { retry_after = 0.05, global = false }, HttpStatusCode.TooManyRequests);
            return Fixture.Json(new { id = "message-1" });
        }))
        using (var notifier = Make(handler, new DiscordNotificationOptions
        {
            Enabled = true, WebhookUrls = First + ";" + First + "?wait=false;" + Second,
            Events = "worker_failed,lane_failed,account_failed"
        }, reports))
        {
            var error = new LotusLmsApiException("unsafe-original-exception", "practice/api/save-take",
                httpDiagnostics: new JsonObject { ["failureKind"] = "application_rejected",
                    ["response"] = new JsonObject { ["statusCode"] = 200, ["body"] = new JsonObject { ["error_code"] = "TAKE_CLOSED", ["token"] = "[REDACTED]" } } });
            var failure = Failure with { Reason = "LMS từ chối yêu cầu.", Details = LotusLMS.Worker.V2.Accounts.LmsFailureDiagnostics.Context(error) };
            await Task.WhenAll(notifier.NotifyAsync(failure), notifier.NotifyAsync(failure));
            check(seen.Count == 4 && attempts.Count == 2 && attempts.Values.All(count => count == 2),
                "fanout deduplicates URLs and concurrent incidents while retrying 5xx and 429 independently");
            check(seen.Select(entry => entry.Record["eventId"]!.GetValue<string>()).Distinct().Count() == 1,
                "all destinations and retries retain the same logical event ID");
            check(seen.All(entry => entry.Payload["content"]!.GetValue<string>().Length <= 2000
                && entry.Payload["allowed_mentions"]!["parse"]!.AsArray().Count == 0
                && entry.Record["details"]!["http"]!["response"]!["body"]!["error_code"]!.GetValue<string>() == "TAKE_CLOSED"
                && !entry.Record.ToJsonString().Contains("unsafe-original-exception")),
                "Discord messages fit the content limit, disable mentions and attach safe HTTP diagnostics");
            await notifier.NotifyAsync(failure);
            check(seen.Count == 4, "the same incident is not emitted again through another stop branch");
            await notifier.NotifyAsync(failure with { RunId = "run-2" });
            check(seen.Count == 6, "a later run still emits its own incident");
        }

        var permanent = new ConcurrentDictionary<string, int>();
        using (var handler = new Fixture((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            permanent.AddOrUpdate(path, 1, (_, old) => old + 1);
            return Task.FromResult(path.Contains("/111/") ? Fixture.Json(new { message = First }, HttpStatusCode.NotFound)
                : Fixture.Json(new { id = "accepted" }));
        }))
        using (var notifier = Make(handler, new DiscordNotificationOptions { Enabled = true, WebhookUrls = First + ";" + Second }, reports))
        {
            await notifier.NotifyAsync(Failure);
            await notifier.NotifyAsync(Failure with { RunId = "run-2" });
            await notifier.NotifyAsync(Failure with { EventType = "account_failed", RunId = "run-3" });
            check(permanent.Values.Order().SequenceEqual([1, 2]),
                "a deleted webhook is disabled, healthy destinations continue and account alerts remain opt-in");
            check(reports.All(message => !message.Contains("fixture-hook") && !message.Contains("https://")),
                "delivery logs never print webhook tokens, URLs or Discord response bodies");
        }

        var healthyDelivered = false;
        using (var handler = new Fixture(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/111/"))
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            healthyDelivered = true;
            return Fixture.Json(new { id = "accepted" });
        }))
        using (var notifier = Make(handler, new DiscordNotificationOptions
        {
            Enabled = true, WebhookUrls = First + ";" + Second, RequestTimeoutSeconds = 1,
            DeliveryTimeoutSeconds = 2, MaxRetries = 0
        }, reports))
        {
            var started = Stopwatch.StartNew();
            await notifier.NotifyAsync(Failure);
            check(healthyDelivered && started.Elapsed < TimeSpan.FromSeconds(4),
                "a hanging webhook times out independently and does not block a healthy destination");
        }

        var limitedCalls = 0;
        using (var handler = new Fixture((_, _) =>
        {
            limitedCalls++;
            return Task.FromResult(Fixture.Json(new { retry_after = 60, global = true }, HttpStatusCode.TooManyRequests));
        }))
        using (var notifier = Make(handler, new DiscordNotificationOptions
        {
            Enabled = true, WebhookUrls = First, DeliveryTimeoutSeconds = 1, MaxRetries = 2
        }, reports))
        {
            var started = Stopwatch.StartNew();
            await notifier.NotifyAsync(Failure);
            check(limitedCalls == 1 && started.Elapsed < TimeSpan.FromSeconds(3),
                "long Discord rate limits respect the total delivery budget rather than retrying early or blocking shutdown");
        }

        var globalCalls = 0;
        using (var handler = new Fixture((_, _) =>
        {
            globalCalls++;
            return Task.FromResult(globalCalls == 1
                ? Fixture.Json(new { retry_after = 0.2, global = true }, HttpStatusCode.TooManyRequests)
                : Fixture.Json(new { id = "accepted" }));
        }))
        using (var notifier = Make(handler, new DiscordNotificationOptions
            { Enabled = true, WebhookUrls = First, MaxRetries = 0 }, reports))
        {
            await notifier.NotifyAsync(Failure);
            var started = Stopwatch.StartNew();
            await notifier.NotifyAsync(Failure with { RunId = "run-2" });
            check(globalCalls == 2 && started.Elapsed >= TimeSpan.FromMilliseconds(100),
                "a global retry_after deadline is retained between incidents");
        }

        using (var handler = new Fixture((_, _) => throw new Exception("Invalid config must not send")))
        using (var notifier = Make(handler, new DiscordNotificationOptions
            { Enabled = true, WebhookUrls = First, RequestTimeoutSeconds = 0 }, reports))
        {
            await notifier.NotifyAsync(Failure);
            check(reports.Any(message => message.Contains("Discord:RequestTimeoutSeconds")),
                "invalid Discord settings disable delivery and identify the field without stopping the worker");
        }

        using (var handler = new Fixture((_, _) => throw new Exception("Null config must not send")))
        using (var notifier = Make(handler, new DiscordNotificationOptions
            { Enabled = true, WebhookUrls = null!, Events = null! }, reports))
        {
            await notifier.NotifyAsync(Failure);
            check(true, "explicit JSON null notification settings do not prevent the worker from starting");
        }
    }

    private static DiscordWorkerNotifier Make(HttpMessageHandler handler, DiscordNotificationOptions settings,
        ConcurrentQueue<string> reports) =>
        new(new HttpClient(handler, disposeHandler: false), settings, "server-test", "worker-test", TimeProvider.System, reports.Enqueue);

    private static async Task<(JsonObject Payload, JsonObject Record)> ReadAsync(HttpRequestMessage request)
    {
        var parts = (MultipartFormDataContent)request.Content!;
        var payload = parts.Single(part => part.Headers.ContentDisposition!.Name!.Trim('"') == "payload_json");
        var file = parts.Single(part => part.Headers.ContentDisposition!.Name!.Trim('"') == "files[0]");
        return (JsonNode.Parse(await payload.ReadAsStringAsync())!.AsObject(), JsonNode.Parse(await file.ReadAsStringAsync())!.AsObject());
    }
}
