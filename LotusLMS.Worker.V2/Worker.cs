using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Worker.V2.Accounts;
using LotusLMS.Worker.V2.LotusRunner.CheckingTime;
using LotusLMS.Worker.V2.Notifications;
using Microsoft.Extensions.Options;

namespace LotusLMS.Worker.V2;

internal sealed class Worker(
    IOptions<WorkerOptions> options,
    IOptions<LotusClientOptions> clientDefaults,
    IOptions<LotusLmsOptions> lmsDefaults,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    IHostApplicationLifetime applicationLifetime,
    IWorkerNotifier notifier) : BackgroundService
{
    private readonly ILogger<Worker> _logger = loggerFactory.CreateLogger<Worker>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RunCoordinatorAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            _logger.LogCritical("Worker coordinator stopped unexpectedly with {ErrorType}.", error.GetType().Name);
            await notifier.NotifyAsync(WorkerFailure.FromException("worker_failed", "COORDINATOR_FAILED",
                "Worker dừng do lỗi ngoài quy trình xử lý lane.", error));
            Environment.ExitCode = 1;
            applicationLifetime.StopApplication();
        }
    }

    private async Task RunCoordinatorAsync(CancellationToken stoppingToken)
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
                    loggerFactory,
                    notifier));
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
            await notifier.NotifyAsync(WorkerFailure.FromException("worker_failed", "REGISTRATION_FAILED",
                "Không đăng ký được các lane với Client API; worker dừng và không nhận account.", error));
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
            await notifier.NotifyAsync(new WorkerFailure("worker_failed", "ALL_LANES_STOPPED",
                $"Toàn bộ {lanes.Count} lane đã dừng do lỗi; worker sẽ thoát.",
                Details: new Dictionary<string, object?> { ["laneCount"] = lanes.Count }));
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
    ILoggerFactory loggerFactory,
    IWorkerNotifier notifier)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("WorkerLane." + workerName);
    private readonly AccountWebProxyProvider _proxyProvider = new();

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try { await RunLaneAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            _logger.LogError("Lane {WorkerName} stopped unexpectedly with {ErrorType}.", workerName, error.GetType().Name);
            await notifier.NotifyAsync(WorkerFailure.FromException("lane_failed", "LANE_UNEXPECTED_FAILURE",
                "Lane dừng do lỗi ngoài quy trình xử lý account.", error, workerName));
        }
    }

    private async Task RunLaneAsync(CancellationToken stoppingToken)
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
                await notifier.NotifyAsync(WorkerFailure.FromException("lane_failed", "RECEIVE_FAILED",
                    "Lane không thể tiếp nhận account an toàn và đã dừng.", error, workerName));
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
            using var accountScope = _logger.BeginScope(new AccountLogScope(
                workerName, context.AccountId, context.Ownership.RunId, context.EnrolmentPlanIid));
            var settings = workerOptions.Value;
            if (settings.WorkRestEnabled)
                context.ScheduleRest(TimeSpan.FromMinutes(settings.ContinuousWorkMinutes), settings.PickRestSeconds());
            var leaseTask = lease.RunAsync(context, stoppingToken);
            var outcome = "stopped";
            var reason = context.ValidateConfiguration();
            var stopLane = false;
            LotusLmsApiException? lmsFailure = null;
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
                            AccountStudyResult.SelectedSectionsCompleted => "completed",
                            AccountStudyResult.DailyKpiReached => "deferred",
                            _ => "failed"
                        };
                        reason = result switch
                        {
                            AccountStudyResult.Completed => "Đã hoàn thành chương trình học.",
                            AccountStudyResult.SelectedSectionsCompleted => "Đã hoàn thành các phần được chọn chạy.",
                            AccountStudyResult.DailyKpiReached => "Đã đạt KPI trong ngày; account được hẹn chạy lại vào giờ ngẫu nhiên trong khung 00:00–08:00 ngày mai (giờ Việt Nam).",
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
                        (outcome, reason) = CancellationResult(context, stoppingToken);
                    }
                    catch (Exception error)
                    {
                        outcome = "failed";
                        lmsFailure = error as LotusLmsApiException;
                        reason = GetSafeFailureReason(error);
                        LogAccountFailure(error, context.AccountId);
                    }

                    context.Stop(reason);
                    await TrySendFinalLogAsync(context, logs, outcome, reason, stoppingToken, lmsFailure);
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                (outcome, reason) = CancellationResult(context, stoppingToken);
                await TrySendFinalLogAsync(context, logs, outcome, reason, stoppingToken);
            }
            catch (Exception error)
            {
                outcome = "failed";
                lmsFailure = error as LotusLmsApiException;
                reason = GetSafeFailureReason(error);
                LogAccountFailure(error, context.AccountId);
                await TrySendFinalLogAsync(context, logs, outcome, reason, stoppingToken, lmsFailure);
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
                if (!stoppingToken.IsCancellationRequested)
                    await notifier.NotifyAsync(new WorkerFailure("lane_failed", "FINISH_UNCONFIRMED",
                        "Không xác nhận được finish với Client API; lane dừng để tránh nhận account khác. " + reason,
                        workerName, context.AccountId, context.Ownership.RunId,
                        lmsFailure is null ? null : LmsFailureDiagnostics.Context(lmsFailure)));
                return true;
            }

            receiver.Release();
            if (stopLane)
            {
                _logger.LogError("Lane {WorkerName} stopped after a proxy IP verification failure.", workerName);
                if (!stoppingToken.IsCancellationRequested)
                    await notifier.NotifyAsync(new WorkerFailure("lane_failed", "PROXY_IP_VERIFICATION_FAILED",
                        reason ?? "Không xác minh được IP proxy.", workerName, context.AccountId, context.Ownership.RunId));
            }
            else if (outcome == "failed" && !stoppingToken.IsCancellationRequested)
                await notifier.NotifyAsync(new WorkerFailure("account_failed", "ACCOUNT_FAILED",
                    reason ?? "Account kết thúc do lỗi.", workerName, context.AccountId, context.Ownership.RunId,
                    lmsFailure is null ? null : LmsFailureDiagnostics.Context(lmsFailure)));
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
                    token,
                    context.RunSections,
                    context.ReportCurrentLesson);
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
            Outcome = outcome,
            RestSeconds = outcome == "resting" ? context.ScheduledRestSeconds : null
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
        CancellationToken stoppingToken,
        LotusLmsApiException? lmsFailure = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await logs.SendAsync(context, outcome == "failed" ? "error" : "info", reason, timeout.Token,
                lmsFailure is null ? null : LmsFailureDiagnostics.Context(lmsFailure));
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
        LotusLmsApiException lmsError => LmsFailureDiagnostics.Describe(lmsError),
        _ => $"Lỗi xử lý account ({error.GetType().Name})."
    };

    private static (string Outcome, string Reason) CancellationResult(AccountRunContext context, CancellationToken stoppingToken) =>
        stoppingToken.IsCancellationRequested ? ("stopped", "Worker đang shutdown.")
            : context.ScheduledRestSeconds is not null && !context.LeaseLost
                ? ("resting", context.StopReason) : ("stopped", context.StopReason);

    private void LogAccountFailure(Exception error, string accountId)
    {
        if (error is LotusLmsApiException lmsError)
        {
            _logger.LogError(
                "Account {AccountId} failed in lane {WorkerName}: {Failure} HTTP_REQUEST_FAILED {HttpDetails}",
                accountId,
                workerName,
                LmsFailureDiagnostics.Describe(lmsError),
                System.Text.Json.JsonSerializer.Serialize(LmsFailureDiagnostics.Context(lmsError)));
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
