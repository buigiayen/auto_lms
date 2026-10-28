using System.Globalization;
using System.Net;
using LotusLMS.Client;

namespace LotusLMS.Worker.V2.Accounts;

internal static class WorkerProxyConfiguration
{
    public static bool TryCreateWebProxy(ClientProxy? config, DateTimeOffset serverTime, out WebProxy? proxy)
    {
        proxy = null;
        if (config is null) return true;
        if (!config.Enabled || config.ExpiresAt is { } expiresAt && expiresAt <= serverTime)
            return false;

        string? username = config.Username;
        string? password = config.Password;
        Uri? address;
        if (TryParseProviderAddress(config.Address, out address, out var providerUsername, out var providerPassword))
        {
            username = string.IsNullOrEmpty(username) ? providerUsername : username;
            password = string.IsNullOrEmpty(password) ? providerPassword : password;
        }
        else if (Uri.TryCreate(config.Address, UriKind.Absolute, out var uri)
            && (uri.Scheme is "http" or "https" or "socks5")
            && !string.IsNullOrWhiteSpace(uri.Host)
            && uri.Port is >= 1 and <= 65535
            && uri.UserInfo.Length == 0
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0
            && uri.AbsolutePath is "" or "/")
        {
            address = uri;
        }
        else
        {
            return false;
        }

        proxy = new WebProxy(address!)
        {
            Credentials = string.IsNullOrEmpty(username) ? null : new NetworkCredential(username, password)
        };
        return true;
    }

    private static bool TryParseProviderAddress(
        string? value,
        out Uri? address,
        out string username,
        out string password)
    {
        address = null;
        username = "";
        password = "";
        if (string.IsNullOrWhiteSpace(value)) return false;

        var scheme = "http";
        foreach (var supportedScheme in new[] { "http", "https", "socks5" })
        {
            var prefix = supportedScheme + "://";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            scheme = supportedScheme;
            value = value[prefix.Length..];
            break;
        }
        if (value.Length == 0) return false;

        var hostEnd = 0;
        if (value[0] == '[')
        {
            hostEnd = value.IndexOf(']');
            if (hostEnd <= 1 || hostEnd + 1 >= value.Length || value[hostEnd + 1] != ':')
                return false;
        }
        else
        {
            hostEnd = value.IndexOf(':');
            if (hostEnd <= 0) return false;
        }

        var portStart = hostEnd + (value[0] == '[' ? 2 : 1);
        var usernameSeparator = value.IndexOf(':', portStart);
        var passwordSeparator = usernameSeparator < 0 ? -1 : value.IndexOf(':', usernameSeparator + 1);
        if (usernameSeparator < 0 || passwordSeparator < 0) return false;

        var host = (value[0] == '[' ? value[1..hostEnd] : value[..hostEnd]).Trim();
        if (!int.TryParse(value.AsSpan(portStart, usernameSeparator - portStart),
                NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
            return false;

        username = value[(usernameSeparator + 1)..passwordSeparator].Trim();
        password = value[(passwordSeparator + 1)..];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username)) return false;

        var formattedHost = host.Contains(':') ? $"[{host}]" : host;
        if (!Uri.TryCreate($"{scheme}://{formattedHost}:{port}", UriKind.Absolute, out var parsed)
            || parsed.Scheme != scheme
            || parsed.Port != port
            || parsed.UserInfo.Length > 0)
            return false;

        address = parsed;
        return true;
    }
}
