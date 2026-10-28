using System.Net;
using LotusLMS.Client;

namespace LotusLMS.Worker.V2.Accounts;

internal sealed class AccountReceiver(
    ILotusClient client, string workerName, TimeProvider clock, AccountWebProxyProvider proxyProvider,
    ILogger<AccountReceiver> logger)
{
    private readonly string _instanceId = Guid.NewGuid().ToString();
    private string? _accountId;
    private string? _requestId;

    public async Task<AccountRunContext?> ReceiveAsync(CancellationToken token)
    {
        if (_accountId is null)
        {
            var page = await client.GetAccountsAsync(new ClientAccountQuery { PageSize = 20 }, token);
            var account = page.Data?.FirstOrDefault(account => account?.Status == "running");
            if (account is null) return null;
            if (!Guid.TryParse(account.Id, out _)) throw new InvalidDataException("Danh sách account có mã không hợp lệ.");
            _accountId = account.Id;
            _requestId = Guid.NewGuid().ToString();
        }

        var started = clock.GetTimestamp();
        try
        {
            var assignment = await client.ClaimAsync(_accountId, new ClientClaimRequest
            {
                WorkerName = workerName,
                InstanceId = _instanceId,
                RequestId = _requestId!
            }, token);
            var accountDetails = await client.GetAccountAsync(_accountId, token);
            if (accountDetails.Id != _accountId || accountDetails.ProxyId != assignment.Proxy?.Id)
                throw new InvalidDataException("Account details do not match the claimed proxy assignment.");

            ClientProxy? proxy = null;
            if (accountDetails.ProxyId is { Length: > 0 } proxyId)
            {
                proxy = await client.GetProxyAsync(
                    proxyId,
                    _accountId,
                    new ClientRunOwnership
                    {
                        RunId = assignment.RunId,
                        InstanceId = assignment.InstanceId,
                        LeaseToken = assignment.LeaseToken
                    },
                    token);
                if (proxy.Id != proxyId)
                    throw new InvalidDataException("Proxy details do not match the account assignment.");
            }
            proxyProvider.Apply(proxy, assignment.ServerTime);

            // Reconcile a lost heartbeat/finish response with the same claim before receiving another account.
            return new AccountRunContext(
                assignment,
                proxy,
                _accountId,
                workerName,
                _instanceId,
                started,
                clock,
                accountDetails.IdentityNumber);
        }
        catch (LotusClientException error) when (error.Code is "ACCOUNT_CLAIMED" or "ACCOUNT_NOT_QUEUED"
            or "ACCOUNT_DEFERRED" or "CLAIM_CLOSED" or "PROXY_EXPIRED" or "PROXY_DISABLED"
            || error.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation("Account {AccountId} không còn sẵn sàng ({Code}); tải lại hàng chờ.", _accountId, error.Code);
            Release();
            return null;
        }
        catch (LotusClientException error) when (error.Code == "WORKER_INSTANCE_BUSY")
        {
            throw new InvalidDataException("Tiến trình đang có lượt khác chưa xác định được; dừng nhận account mới.");
        }
    }

    public void Release() { _accountId = null; _requestId = null; }
}
