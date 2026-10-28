namespace LotusLMS.Client;

/// <summary>Typed access to the Node.js client worker queue API. Does not run LMS jobs or retry requests.</summary>
public interface ILotusClient
{
    Task<ClientAccountPage> GetAccountsAsync(ClientAccountQuery? query = null, CancellationToken cancellationToken = default);
    Task<ClientAccount> GetAccountAsync(string accountId, CancellationToken cancellationToken = default);
    Task<ClientProxy> GetProxyAsync(
        string proxyId,
        string accountId,
        ClientRunOwnership ownership,
        CancellationToken cancellationToken = default);
    Task<ClientAssignment> ClaimAsync(string accountId, ClientClaimRequest request, CancellationToken cancellationToken = default);
    Task<ClientRunControl> HeartbeatAsync(string accountId, ClientRunOwnership ownership, CancellationToken cancellationToken = default);
    Task<ClientRunControl> GetControlAsync(string accountId, ClientRunOwnership ownership, CancellationToken cancellationToken = default);
    Task<ClientFinishResult> FinishAsync(string accountId, ClientFinishRequest request, CancellationToken cancellationToken = default);
    Task<ClientLogReceipt> SendLogAsync(ClientWorkerLog log, CancellationToken cancellationToken = default);
    Task<ClientWorkerRegistration> RegisterWorkerAsync(string workerName, CancellationToken cancellationToken = default);
}
