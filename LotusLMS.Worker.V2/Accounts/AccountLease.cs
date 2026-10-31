using LotusLMS.Client;
using Microsoft.Extensions.Options;

namespace LotusLMS.Worker.V2.Accounts;

internal sealed class AccountLease(ILotusClient client, IOptions<WorkerOptions> options,
    TimeProvider clock, ILogger<AccountLease> logger)
{
    public async Task RunAsync(AccountRunContext context, CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, context.StoppingToken);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.HeartbeatIntervalSeconds), clock, linked.Token);
                var remaining = context.RemainingLease;
                if (remaining <= TimeSpan.Zero) { context.Stop("Lease đã hết hạn.", true); return; }
                using var request = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                request.CancelAfter(remaining);
                var started = clock.GetTimestamp();
                try
                {
                    var control = await client.HeartbeatAsync(context.AccountId, context.CreateHeartbeat(), request.Token);
                    context.UpdateControl(control, started);
                }
                catch (LotusClientException error) when (BackendErrors.IsTemporary(error))
                {
                    logger.LogWarning("Heartbeat account {AccountId} chưa thành công ({Code}); chỉ retry trong hạn lease.", context.AccountId, error.Code);
                }
                catch (LotusClientException error) when (error.Code is "LEASE_LOST" or "RUN_OWNERSHIP_MISMATCH")
                { context.Stop("Backend xác nhận Worker đã mất quyền sở hữu lượt chạy.", true); return; }
                catch (OperationCanceledException) when (request.IsCancellationRequested && !linked.IsCancellationRequested)
                { context.Stop("Heartbeat vượt quá thời hạn lease.", true); return; }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            context.Stop("Không xác nhận được quyền điều khiển account.", true);
            throw;
        }
    }
}
