namespace LotusLMS.Api;

public sealed class LotusLmsOptions
{
    public const string SectionName = "LotusLms";

    public string BaseUrl { get; set; } = "https://staging-api.lotuslms.com";

    public string WebOrigin { get; set; } = "https://yenthuy.huelms.com";

    public string Tenant { get; set; } = "yenthuy";

    public int RequestTimeoutSeconds { get; set; } = 100;

    public bool LogApiResponses { get; set; } = true;

    public string ApiResponseLogDirectory { get; set; } = "logs/api-responses";

    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";

    public string BrowserPlatform { get; set; } = "Windows";

    public string AcceptLanguage { get; set; } = "en-US,en;q=0.9";

    /// <summary>
    /// Adds Authorization: Bearer in addition to the upstream-specific _sand_token fields.
    /// Keep disabled when strict compatibility with the current web client is required.
    /// </summary>
    public bool SendBearerToken { get; set; }
}
