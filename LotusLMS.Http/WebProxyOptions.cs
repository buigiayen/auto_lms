namespace LotusLMS.Http;

/// <summary>
/// Configuration used to create the proxy for outgoing HTTP connections.
/// </summary>
public sealed class WebProxyOptions
{
    /// <summary>
    /// Default configuration path expected by <see cref="WebProxyServiceCollectionExtensions.AddWebProxySupport"/>.
    /// </summary>
    public const string SectionName = "Http:Proxy";

    public bool Enabled { get; set; }

    public string? Address { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? Domain { get; set; }

    public bool BypassOnLocal { get; set; } = true;

    public bool UseDefaultCredentials { get; set; }
}
