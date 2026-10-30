using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Worker.V2.Accounts;

namespace LotusLMS.Worker.V2.Notifications;

internal interface IWorkerNotifier
{
    Task NotifyAsync(WorkerFailure failure, CancellationToken cancellationToken = default);
}

internal sealed record WorkerFailure(
    string EventType,
    string Code,
    string Reason,
    string? LaneName = null,
    string? AccountId = null,
    string? RunId = null,
    IReadOnlyDictionary<string, object?>? Details = null)
{
    // Error messages from HTTP libraries may contain credentials or signed URLs.
    internal static WorkerFailure FromException(string eventType, string code, string reason, Exception error,
        string? laneName = null) => new(eventType, code, reason, laneName, Details:
        error is LotusLmsApiException lms ? LmsFailureDiagnostics.Context(lms)
            : new Dictionary<string, object?>
            {
                ["exceptionType"] = error.GetType().Name,
                ["clientErrorCode"] = error is LotusClientException client ? client.Code : null
            });
}
