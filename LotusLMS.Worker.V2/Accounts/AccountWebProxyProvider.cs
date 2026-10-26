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

        if (!WorkerProxyConfiguration.TryCreateWebProxy(config, serverTime, out var proxy))
        {
            throw new InvalidDataException("Account proxy is disabled, expired or invalid.");
        }

        _proxy.Address = proxy!.Address;
        _proxy.Credentials = proxy.Credentials;
        _proxy.BypassProxyOnLocal = false;
    }
}
