using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Worker.V2.Accounts;
using LotusLMS.Worker.V2.LotusRunner.CheckingTime;
using Microsoft.Extensions.Options;

namespace LotusLMS.Worker.V2;

internal sealed class Worker(
    IOptions<WorkerOptions> options,
    IOptions<LotusClientOptions> clientDefaults,
    IOptions<LotusLmsOptions> lmsDefaults,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    private readonly ILogger<Worker> _logger = loggerFactory.CreateLogger<Worker>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var processingFactory = new AccountProcessingFactory(lmsDefaults, options, clock, loggerFactory);
        var registrationClient = CreateClient(clientDefaults.Value, settings.RegistrationApiKey);
        var lanes = new List<WorkerLane>(settings.MaxConcurrentAccounts);
        try
        {
            for (var number = 1; number <= settings.MaxConcurrentAccounts; number++)
            {
                var laneName = settings.GetLaneWorkerName(number);
                var registration = await registrationClient.RegisterWorkerAsync(laneName, stoppingToken);
                if (registration.WorkerName != laneName || string.IsNullOrWhiteSpace(registration.ApiKey))
                    throw new InvalidDataException($"Client API trả thông tin đăng ký không hợp lệ cho lane {laneName}.");

                lanes.Add(new WorkerLane(
                    laneName,
                    registration.ApiKey,
                    clientDefaults.Value,
                    options,
                    clock,
                    processingFactory,
                    loggerFactory));
                _logger.LogInformation("Lane {WorkerName} registered with Client API.", laneName);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            _logger.LogCritical(error, "Worker lane registration failed; no lane will claim accounts.");
            Environment.ExitCode = 1;
            applicationLifetime.StopApplication();
            return;
        }
        finally
        {
            registrationClient.Dispose();
        }

        _logger.LogInformation(
            "Worker coordinator started {LaneCount} independent lanes for {WorkerName}.",
            lanes.Count,
            settings.WorkerName);

        await Task.WhenAll(lanes.Select(lane => lane.RunAsync(stoppingToken)));

        if (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogCritical("All worker lanes have exited; stopping the host.");
            Environment.ExitCode = 1;
            applicationLifetime.StopApplication();
        }
    }

    private static LotusClient CreateClient(LotusClientOptions defaults, string apiKey)
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false
        })
        {
            BaseAddress = new Uri(defaults.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(defaults.RequestTimeoutSeconds)
        };
        return new LotusClient(http, Options.Create(new LotusClientOptions
        {
            BaseUrl = defaults.BaseUrl,
            ApiKey = apiKey,
            RequestTimeoutSeconds = defaults.RequestTimeoutSeconds
        }));
    }
}

internal sealed class WorkerLane(
    string workerName,
    string apiKey,
    LotusClientOptions clientDefaults,
    IOptions<WorkerOptions> workerOptions,
    TimeProvider clock,
    AccountProcessingFactory processingFactory,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("WorkerLane." + workerName);
    private readonly AccountWebProxyProvider _proxyProvider = new();

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using var backendHttp = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false
        });
        backendHttp.BaseAddress = new Uri(clientDefaults.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        backendHttp.Timeout = TimeSpan.FromSeconds(clientDefaults.RequestTimeoutSeconds);
        var client = new LotusClient(backendHttp, Options.Create(new LotusClientOptions
        {
            BaseUrl = clientDefaults.BaseUrl,
            ApiKey = apiKey,
            RequestTimeoutSeconds = clientDefaults.RequestTimeoutSeconds
        }));
        var receiver = new AccountReceiver(
            client, workerName, clock, _proxyProvider, loggerFactory.CreateLogger<AccountReceiver>());
        var lease = new AccountLease(client, workerOptions, clock, loggerFactory.CreateLogger<AccountLease>());
        var logs = new AccountLog(client, clock, loggerFactory.CreateLogger<AccountLog>());

        _logger.LogInformation("Lane {WorkerName} started.", workerName);
        while (!stoppingToken.IsCancellationRequested)
        {
            AccountRunContext? context;
            try
            {
                context = await receiver.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (LotusClientException error) when (BackendErrors.IsTemporary(error))
            {
                _logger.LogWarning("Lane {WorkerName} could not poll the queue ({Code}); retrying.", workerName, error.Code);
                await DelayBeforePollAsync(stoppingToken);
                continue;
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Lane {WorkerName} stopped because it cannot safely receive accounts.", workerName);
                return;
            }

            if (context is null)
            {
                await DelayBeforePollAsync(stoppingToken);
                continue;
            }

            var stopLane = await ProcessAccountAsync(context, receiver, lease, logs, client, stoppingToken);
            if (stopLane)
            {
                _logger.LogError("Lane {WorkerName} stopped because it cannot safely continue.", workerName);
                return;
            }
        }

        _logger.LogInformation("Lane {WorkerName} stopped.", workerName);
    }

    private async Task<bool> ProcessAccountAsync(
        AccountRunContext context,
        AccountReceiver receiver,
        AccountLease lease,
        AccountLog logs,
        ILotusClient client,
        CancellationToken stoppingToken)
    {
        using (context)
        {
            var leaseTask = lease.RunAsync(context, stoppingToken);
            var outcome = "stopped";
            var reason = context.ValidateConfiguration();
            var stopLane = false;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                context.StoppingToken);
            try
            {
                if (reason is not null)
                {
                    outcome = "failed";
                    await logs.SendAsync(context, "error", reason, linked.Token);
                }
                else
                {
                    await logs.SendAsync(
                        context,
                        "info",
                        $"Đã tiếp nhận {context.IdentityNumber} - {context.EnrolmentPlanName} trên {workerName}.",
                        linked.Token);

                    using var processing = processingFactory.Create(context, _proxyProvider);
                    try
                    {
                        var result = await RunWithRetryAsync(
                            processing.CheckingTimeService,
                            context,
                            logs,
                            linked.Token);
                        outcome = result switch
                        {
                            AccountStudyResult.Completed => "completed",
                            AccountStudyResult.DailyKpiReached => "deferred",
                            _ => "failed"
                        };
                        reason = result switch
                        {
                            AccountStudyResult.Completed => "Đã hoàn thành chương trình học.",
                            AccountStudyResult.DailyKpiReached => "Đã đạt KPI trong ngày; account được tạm hoãn đến 00:00 ngày mai (giờ Việt Nam).",
                            _ => "Quy trình học chưa hoàn tất."
                        };
                    }
                    catch (ProxyIpCheckException error)
                    {
                        outcome = "failed";
                        reason = error.Message;
                        stopLane = true;
                    }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested)
                    {
                        reason = context.StopReason;
                        outcome = "stopped";
                    }
                    catch (Exception error)
                    {
                        outcome = "failed";
                        reason = GetSafeFailureReason(error);
                        LogAccountFailure(error, context.AccountId);
                    }

                    context.Stop(reason);
                    await TrySendFinalLogAsync(context, logs, outcome, reason, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                outcome = "stopped";
                reason = context.StoppingToken.IsCancellationRequested
                    ? context.StopReason
                    : "Worker đang shutdown.";
                await TrySendFinalLogAsync(context, logs, outcome, reason, stoppingToken);
            }
            catch (Exception error)
            {
                outcome = "failed";
                reason = GetSafeFailureReason(error);
                LogAccountFailure(error, context.AccountId);
                await TrySendFinalLogAsync(context, logs, outcome, reason, stoppingToken);
            }
            finally
            {
                context.Stop(reason ?? "Đã kết thúc lượt.");
                try
                {
                    await leaseTask;
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Lease monitor ended for account {AccountId}.", context.AccountId);
                }
            }

            if (!await FinishAsync(context, client, outcome))
            {
                _logger.LogCritical(
                    "Lane {WorkerName} could not confirm finish for account {AccountId}; stopping to avoid taking another account.",
                    workerName,
                    context.AccountId);
                return true;
            }

            receiver.Release();
            if (stopLane)
                _logger.LogError("Lane {WorkerName} stopped after a proxy IP verification failure.", workerName);
            return stopLane;
        }
    }

    private async Task<AccountStudyResult> RunWithRetryAsync(
        CheckingTimeService checkingTime,
        AccountRunContext context,
        AccountLog logs,
        CancellationToken token)
    {
        while (true)
        {
            context.EnsureCanUse();
            try
            {
                return await checkingTime.Runner(
                    context.CreateLoginRequest(),
                    context.EnrolmentPlanIid!.Value,
                    context.HasProxy,
                    (message, cancellationToken) => logs.SendAsync(context, "info", message, cancellationToken),
                    token);
            }
            catch (LotusClientException error) when (BackendErrors.IsTemporary(error)
                && context.RemainingLease > TimeSpan.FromSeconds(2))
            {
                _logger.LogWarning(
                    "Temporary LMS failure for account {AccountId} ({Code}); retrying within the lease.",
                    context.AccountId,
                    error.Code);
                await Task.Delay(TimeSpan.FromSeconds(1), clock, token);
            }
        }
    }

    private async Task<bool> FinishAsync(
        AccountRunContext context,
        ILotusClient client,
        string outcome)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var request = new ClientFinishRequest
        {
            RunId = context.Ownership.RunId,
            InstanceId = context.Ownership.InstanceId,
            LeaseToken = context.Ownership.LeaseToken,
            Outcome = outcome
        };
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var result = await client.FinishAsync(context.AccountId, request, timeout.Token);
                return result.RunId == context.Ownership.RunId;
            }
            catch (LotusClientException error) when (BackendErrors.IsTemporary(error))
            {
                _logger.LogWarning("Finish for account {AccountId} failed temporarily ({Code}); retrying.", context.AccountId, error.Code);
                try { await Task.Delay(TimeSpan.FromSeconds(1), clock, timeout.Token); }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Finish could not be confirmed for account {AccountId}.", context.AccountId);
                return false;
            }
        }

        return false;
    }

    private async Task TrySendFinalLogAsync(
        AccountRunContext context,
        AccountLog logs,
        string outcome,
        string reason,
        CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await logs.SendAsync(context, outcome == "failed" ? "error" : "info", reason, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogWarning("Final log timed out for account {AccountId}.", context.AccountId);
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Could not send final log for account {AccountId}.", context.AccountId);
        }
    }

    private string GetSafeFailureReason(Exception error) => error switch
    {
        LotusClientException clientError => $"Lỗi Client API ({clientError.Code}).",
        LotusLmsApiException lmsError => $"Lỗi LMS tại {lmsError.Endpoint} ({lmsError.StatusCode?.ToString() ?? "network"}).",
        _ => $"Lỗi xử lý account ({error.GetType().Name})."
    };

    private void LogAccountFailure(Exception error, string accountId)
    {
        if (error is LotusLmsApiException lmsError)
        {
            _logger.LogError(
                "Account {AccountId} failed in lane {WorkerName} at LMS endpoint {Endpoint} ({StatusCode}).",
                accountId,
                workerName,
                lmsError.Endpoint,
                lmsError.StatusCode);
            return;
        }

        _logger.LogError(
            "Account {AccountId} failed in lane {WorkerName} with {ErrorType}.",
            accountId,
            workerName,
            error.GetType().Name);
    }

    private async Task DelayBeforePollAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(workerOptions.Value.PollIntervalSeconds), clock, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
