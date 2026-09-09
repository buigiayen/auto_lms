namespace LotusLMS.Worker;

public sealed class LotusLmsCredentialsOptions
{
    public const string SectionName = "LotusLmsCredentials";

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string? RequestId { get; init; }

    public string? RequestToken { get; init; }

    public string? StudentRequestId { get; init; }

    public string? StudentRequestToken { get; init; }

    public string? ClientSyncToken { get; init; }

    /// <summary>
    /// Optional fixed device id. When omitted, the API library generates a new GUID
    /// for each login. Set it to the web client's fixed value when the tenant binds
    /// sessions to the device id.
    /// </summary>
    public string? DeviceUuid { get; init; }

    /// <summary>
    /// Optional fixed browser-session id. When omitted, the API library generates a
    /// new GUID for each login.
    /// </summary>
    public string? SessionId { get; init; }
}
