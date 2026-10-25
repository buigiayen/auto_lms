using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LotusLMS.Http;
using LotusLMS.Client;
using Microsoft.Extensions.Options;

namespace LotusLMS.Worker;

// The handler receives this same proxy before the client grants permission to work.
// Once work starts, proxy changes require a restart to avoid reusing old connections.
internal sealed class ClientAccountProxyProvider : IWebProxyProvider
{
    private readonly bool _clientEnabled;
    private readonly IWebProxyProvider? _standalone;
    private readonly WebProxy _accountProxy = new();
    private string? _fingerprint;

    public ClientAccountProxyProvider(IOptions<ClientWorkerOptions> client, IOptionsMonitor<WebProxyOptions> proxy)
        : this(client.Value.Enabled, new ConfiguredWebProxyProvider(proxy)) { }

    internal ClientAccountProxyProvider(bool clientEnabled, IWebProxyProvider? standalone = null)
    {
        _clientEnabled = clientEnabled;
        _standalone = standalone;
    }

    public IWebProxy? GetProxy() => _clientEnabled ? _accountProxy : _standalone?.GetProxy();

    internal bool Apply(ClientProxy? config)
    {
        Uri? address = null;
        if (config is not null && (!config.Enabled || config.ExpiresAt <= DateTimeOffset.UtcNow
            || !Uri.TryCreate(config.Address, UriKind.Absolute, out address)
            || address.Scheme is not ("http" or "https") || address.UserInfo.Length > 0))
            throw new InvalidDataException("Account proxy is disabled, expired or invalid.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config))));
        if (_fingerprint is not null) return _fingerprint == fingerprint;
        _accountProxy.Address = address;
        _accountProxy.BypassProxyOnLocal = false;
        _accountProxy.Credentials = string.IsNullOrEmpty(config?.Username) ? null
            : new NetworkCredential(config.Username, config.Password);
        _fingerprint = fingerprint;
        return true;
    }
}
