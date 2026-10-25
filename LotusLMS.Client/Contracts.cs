namespace LotusLMS.Client;

public sealed class ClientAccountQuery
{
    public string? AccountId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class ClientAccountPage
{
    public List<ClientAccount> Data { get; init; } = [];
    public ClientPagination Pagination { get; init; } = new();
}

public sealed class ClientPagination
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
}

public sealed class ClientAccount
{
    public string Id { get; init; } = "";
    public string IdentityNumber { get; init; } = "";
    public string Provider { get; init; } = "";
    public string ManagingTeacher { get; init; } = "";
    public int? EnrolmentPlanIid { get; init; }
    public string EnrolmentPlanName { get; init; } = "";
    public string WorkerName { get; init; } = "";
    public string Status { get; init; } = "";
    public string? ProxyId { get; init; }
    public ClientAccountProxy? Proxy { get; init; }
    public ClientExecution? Execution { get; init; }
}

public sealed class ClientAccountProxy
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public bool Enabled { get; init; }
    public string? ExpiresOn { get; init; }
    public bool IsExpired { get; init; }
}

public sealed class ClientExecution
{
    public string State { get; init; } = "";
    public string? RunId { get; init; }
    public string? WorkerName { get; init; }
    public DateTimeOffset? LastHeartbeatAt { get; init; }
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    public ClientActivity? LastLog { get; init; }
}

public sealed class ClientActivity
{
    public string Message { get; init; } = "";
    public string Level { get; init; } = "";
    public DateTimeOffset OccurredAt { get; init; }
}

public sealed class ClientClaimRequest
{
    public required string WorkerName { get; init; }
    public required string InstanceId { get; init; }
    /// <summary>Keep this ID unchanged when retrying a claim whose response was lost.</summary>
    public required string RequestId { get; init; }
}

// Classes deliberately do not generate ToString() output containing secrets.
public class ClientRunOwnership
{
    public required string RunId { get; init; }
    public required string InstanceId { get; init; }
    public required string LeaseToken { get; init; }
}

public sealed class ClientFinishRequest : ClientRunOwnership
{
    public required string Outcome { get; init; }
}

public class ClientRunControl
{
    public int? EnrolmentPlanIid { get; init; }
    public string EnrolmentPlanName { get; init; } = "";
    public string AccountId { get; init; } = "";
    public string WorkerName { get; init; } = "";
    public string RunId { get; init; } = "";
    public string State { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset ServerTime { get; init; }
    public DateTimeOffset LeaseExpiresAt { get; init; }
    public ClientProxy? Proxy { get; init; }
}

public sealed class ClientAssignment : ClientRunControl
{
    public string InstanceId { get; init; } = "";
    public string LeaseToken { get; init; } = "";
    public string Provider { get; init; } = "";
    public ClientCredentials? Credentials { get; init; }
    public ClientLmsProvider? Lms { get; init; }
}

public sealed class ClientCredentials
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string? DeviceUuid { get; init; }
}

public sealed class ClientLmsProvider
{
    public string Base { get; init; } = "";
    public string Origin { get; init; } = "";
    public string Tenant { get; init; } = "";
}

public sealed class ClientProxy
{
    public string? Id { get; init; }
    public bool Enabled { get; init; }
    public string? Address { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class ClientWorkerLog : ClientRunOwnership
{
    public required string EventId { get; init; }
    public required string AccountId { get; init; }
    public required string WorkerName { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object?> Context { get; init; } = [];
}

public sealed class ClientLogReceipt
{
    public long Id { get; init; }
    public string EventId { get; init; } = "";
    public bool Duplicate { get; init; }
}

public sealed class ClientFinishResult
{
    public string RunId { get; init; } = "";
    public string State { get; init; } = "";
}

public sealed class ClientWorkerRegistration
{
    public string WorkerName { get; init; } = "";
    public string ApiKey { get; init; } = "";
}
