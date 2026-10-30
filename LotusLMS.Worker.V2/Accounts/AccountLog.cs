using LotusLMS.Client;

namespace LotusLMS.Worker.V2.Accounts;

internal sealed class AccountLog(ILotusClient client, TimeProvider clock, ILogger<AccountLog> logger)
{
    public async Task SendAsync(AccountRunContext context, string level, string message, CancellationToken token,
        IReadOnlyDictionary<string, object?>? failure = null)
    {
        var entry = new ClientWorkerLog
        {
            AccountId = context.AccountId,
            WorkerName = context.WorkerName,
            RunId = context.Ownership.RunId,
            InstanceId = context.Ownership.InstanceId,
            LeaseToken = context.Ownership.LeaseToken,
            EventId = Guid.NewGuid().ToString(),
            OccurredAt = clock.GetUtcNow(),
            Level = level,
            Message = message,
            Context = new() { ["enrolmentPlanIid"] = context.EnrolmentPlanIid, ["provider"] = context.Provider, ["source"] = "worker-v2" }
        };
        if (failure is not null) entry.Context["failure"] = failure;
        logger.Log(level == "error" ? LogLevel.Error : level == "warn" ? LogLevel.Warning : LogLevel.Information,
            "Account {AccountId}, lượt {RunId}: {Message}", context.AccountId, context.Ownership.RunId, message);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { await client.SendLogAsync(entry, token); return; }
            catch (LotusClientException error) when (BackendErrors.IsTemporary(error))
            {
                logger.LogWarning("Chưa gửi được log account {AccountId} ({Code}); retry cùng EventId.", context.AccountId, error.Code);
                await Task.Delay(TimeSpan.FromSeconds(1), clock, token);
            }
        }
    }
}
