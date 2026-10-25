namespace LotusLMS.Worker;

// Blocks new LMS requests and cancels in-flight requests if ownership is lost.
internal sealed class ClientLeaseHandler(ClientWorkerBridge bridge) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var remaining = bridge.RemainingLease();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, bridge.LeaseLost);
        if (remaining != Timeout.InfiniteTimeSpan) linked.CancelAfter(remaining);
        return await base.SendAsync(request, linked.Token);
    }
}
