using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LotusLMS.Worker.V2.Accounts;

internal sealed class ProxyIpCheckException : Exception
{
    public ProxyIpCheckException()
        : base("Không thể xác minh IP qua proxy của account. Worker sẽ dừng; hãy kiểm tra proxy và thử lại.")
    {
    }
}

internal sealed class ProxyIpChecker
{
    private static readonly Uri CheckUri = new("https://api.ipify.org?format=json");
    private readonly Func<HttpMessageHandler> _handlerFactory;

    public ProxyIpChecker(AccountWebProxyProvider proxyProvider)
        : this(proxyProvider, () => CreateHandler(proxyProvider))
    {
    }

    internal ProxyIpChecker(AccountWebProxyProvider proxyProvider, Func<HttpMessageHandler> handlerFactory)
    {
        _handlerFactory = handlerFactory;
    }

    public async Task<IPAddress> GetProxyIpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var handler = _handlerFactory();
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            using var response = await http.GetAsync(CheckUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<IpResponse>(cancellationToken);
            if (result?.Ip is null || !IPAddress.TryParse(result.Ip, out var address))
                throw new InvalidDataException("Proxy IP response is invalid.");
            return address;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException
            or JsonException or InvalidDataException)
        {
            throw new ProxyIpCheckException();
        }
    }

    private static HttpMessageHandler CreateHandler(AccountWebProxyProvider proxyProvider)
    {
        var proxy = proxyProvider.GetProxy();
        return new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = proxy is not null,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
    }

    private sealed class IpResponse
    {
        public string? Ip { get; init; }
    }
}
