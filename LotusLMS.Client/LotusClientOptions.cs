namespace LotusLMS.Client;

/// <summary>Connection settings for the local Node.js client backend.</summary>
public class LotusClientOptions
{
    public const string SectionName = "Client";
    public string BaseUrl { get; set; } = "http://127.0.0.1:3100";
    public string ApiKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 5;

    internal bool IsValid() => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0
        && uri.Fragment.Length == 0 && RequestTimeoutSeconds is >= 1 and <= 300;
}
