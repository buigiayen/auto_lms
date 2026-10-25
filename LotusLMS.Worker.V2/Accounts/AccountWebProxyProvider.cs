using System.Net;
using LotusLMS.Client;
using LotusLMS.Http;

namespace LotusLMS.Worker.V2.Accounts;

internal sealed class AccountWebProxyProvider : IWebProxyProvider
{
    private readonly WebProxy _proxy = new();

    public IWebProxy GetProxy() => _proxy;

    public void Apply(ClientProxy? config, DateTimeOffset serverTime)
    {
        if (config is null)
        {
            _proxy.Address = null;
            _proxy.Credentials = null;
            _proxy.BypassProxyOnLocal = false;
            return;
        }

        if (!config.Enabled
            || !Uri.TryCreate(config.Address, UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https")
            || address.UserInfo.Length > 0
            || address.Query.Length > 0
            || address.Fragment.Length > 0
            || config.ExpiresAt is { } expiresAt && expiresAt <= serverTime)
        {
            throw new InvalidDataException("Account proxy is disabled, expired or invalid.");
        }

        _proxy.Address = address;
        _proxy.Credentials = string.IsNullOrEmpty(config.Username)
            ? null
            : new NetworkCredential(config.Username, config.Password);
        _proxy.BypassProxyOnLocal = false;
    }
}
