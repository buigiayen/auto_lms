using System.Net;
using Microsoft.Extensions.Options;

namespace LotusLMS.Http;

/// <summary>
/// Builds a web proxy from the current <see cref="WebProxyOptions"/> value.
/// </summary>
public sealed class ConfiguredWebProxyProvider(
    IOptionsMonitor<WebProxyOptions> options) : IWebProxyProvider
{
    public IWebProxy? GetProxy()
    {
        var current = options.CurrentValue;

        if (!current.Enabled)
        {
            return null;
        }

        // Address is guaranteed by options validation before a client is created.
        var proxy = new WebProxy(current.Address!, current.BypassOnLocal);

        if (current.UseDefaultCredentials)
        {
            proxy.UseDefaultCredentials = true;
        }
        else if (!string.IsNullOrWhiteSpace(current.Username))
        {
            proxy.Credentials = new NetworkCredential(
                current.Username,
                current.Password,
                current.Domain);
        }

        return proxy;
    }
}
