using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Worker.V2;
using LotusLMS.Worker.V2.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class DiscordSimulationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var configured = Environment.GetEnvironmentVariable("DISCORD_SIMULATION_BASE_URL");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var simulation = new Uri(configured.TrimEnd('/') + "/");
        if (!simulation.IsLoopback || simulation.Scheme != "http")
            throw new InvalidOperationException("Discord simulation must be a loopback HTTP URL.");
        using var control = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        using (var reset = await control.PostAsync(new Uri(simulation, "client/_reset"), null)) reset.EnsureSuccessStatusCode();
        using (var reset = await control.PostAsync(new Uri(simulation, "discord/_reset"), null)) reset.EnsureSuccessStatusCode();
        using var sender = new DiscordWorkerNotifier(new HttpClient(new LocalDiscordHandler(new Uri(simulation, "discord/"))
            { InnerHandler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false } }),
            new DiscordNotificationOptions
            {
                Enabled = true,
                WebhookUrls = "https://discord.com/api/webhooks/111/fixture-hook;https://discord.com/api/webhooks/222/fixture-hook;https://discord.com/api/webhooks/333/fixture-hook"
            }, "simulation-server", "worker-v2", TimeProvider.System, _ => { });
        var recorder = new RecordingNotifier(sender);
        using var lifetime = new TestLifetime();
        var previousExitCode = Environment.ExitCode;
        using var worker = new Worker(Options.Create(new WorkerOptions
        {
            ServerName = "simulation-server", WorkerName = "worker-v2", MaxConcurrentAccounts = 2,
            RegistrationApiKey = "fixture-bootstrap-key-0123456789abcdef", PollIntervalSeconds = 1, HeartbeatIntervalSeconds = 1
        }), Options.Create(new LotusClientOptions { BaseUrl = new Uri(simulation, "client/").AbsoluteUri, RequestTimeoutSeconds = 2 }),
            Options.Create(new LotusLmsOptions { LogApiResponses = false }), TimeProvider.System, NullLoggerFactory.Instance, lifetime, recorder);
        try
        {
            await worker.StartAsync(default);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!lifetime.ApplicationStopping.IsCancellationRequested)
                await Task.Delay(20, deadline.Token);
            await worker.StopAsync(deadline.Token);
            check(recorder.Events.Count(failure => failure.Code == "RECEIVE_FAILED") == 2
                && recorder.Events.Count(failure => failure.Code == "ALL_LANES_STOPPED") == 1
                && recorder.Events.Count == 3 && Environment.ExitCode == 1,
                "real worker emits one event per fatal lane and one host summary, then exits unsuccessfully");
            var discord = await control.GetFromJsonAsync<JsonObject>(new Uri(simulation, "discord/_stats"));
            check(discord!["counts"]!["111"]!.GetValue<int>() == 4
                && discord["counts"]!["222"]!.GetValue<int>() == 4 && discord["counts"]!["333"]!.GetValue<int>() == 1,
                "real HTTP fanout retries 502/429, disables 404 and delivers all three events to both healthy webhooks");
            var client = await control.GetFromJsonAsync<JsonObject>(new Uri(simulation, "client/_stats"));
            check(client!["registrations"]!.AsArray().Count == 2, "the simulated path exercised both real lane registrations");
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(cleanup.Token);
            Environment.ExitCode = previousExitCode;
        }
    }

    // Test-only rewrite: production URL validation remains restricted to Discord HTTPS,
    // while the actual HttpClient serializes and sends multipart requests to a local stand-in.
    private sealed class LocalDiscordHandler(Uri destination) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host != "discord.com") throw new InvalidOperationException("Unexpected simulation host.");
            request.RequestUri = new Uri(destination, request.RequestUri.PathAndQuery.TrimStart('/'));
            return base.SendAsync(request, token);
        }
    }

    private sealed class RecordingNotifier(IWorkerNotifier sender) : IWorkerNotifier
    {
        public ConcurrentQueue<WorkerFailure> Events { get; } = new();
        public Task NotifyAsync(WorkerFailure failure, CancellationToken token = default)
        {
            Events.Enqueue(failure);
            return sender.NotifyAsync(failure, token);
        }
    }

    private sealed class TestLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => default;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => default;
        public void StopApplication() => _stopping.Cancel();
        public void Dispose() => _stopping.Dispose();
    }
}
