using System.Text.RegularExpressions;

namespace LotusLMS.Worker.V2.Notifications;

internal sealed class DiscordNotificationOptions
{
    public const string SectionName = "Discord";
    public bool Enabled { get; set; }
    public string WebhookUrls { get; set; } = "";
    public string Events { get; set; } = "worker_failed,lane_failed";
    public int RequestTimeoutSeconds { get; set; } = 5;
    public int DeliveryTimeoutSeconds { get; set; } = 15;
    public int MaxRetries { get; set; } = 2;

    internal IEnumerable<string> ValidationErrors()
    {
        if (RequestTimeoutSeconds is < 1 or > 30) yield return "Discord:RequestTimeoutSeconds phải từ 1 đến 30.";
        if (DeliveryTimeoutSeconds is < 1 or > 60) yield return "Discord:DeliveryTimeoutSeconds phải từ 1 đến 60.";
        if (MaxRetries is < 0 or > 5) yield return "Discord:MaxRetries phải từ 0 đến 5.";
    }

    internal static bool TryWebhook(string value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps || !candidate.IsDefaultPort
            || candidate.UserInfo.Length != 0 || candidate.Fragment.Length != 0
            || candidate.Host is not ("discord.com" or "discordapp.com" or "canary.discord.com" or "ptb.discord.com")
            || !Regex.IsMatch(candidate.AbsolutePath, @"^/api(?:/v[0-9]+)?/webhooks/[0-9]+/[A-Za-z0-9._-]+/?$"))
            return false;
        var query = candidate.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !Uri.UnescapeDataString(part.Split('=', 2)[0]).Equals("wait", StringComparison.OrdinalIgnoreCase));
        uri = new UriBuilder(candidate) { Query = string.Join("&", query.Append("wait=true")) }.Uri;
        return true;
    }
}
