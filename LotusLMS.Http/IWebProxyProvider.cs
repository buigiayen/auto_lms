using System.Net;

namespace LotusLMS.Http;

/// <summary>
/// Resolves the proxy to use for an outgoing HTTP handler.
/// </summary>
public interface IWebProxyProvider
{
    /// <summary>
    /// Returns the configured proxy, or <see langword="null"/> when proxying is disabled.
    /// </summary>
    IWebProxy? GetProxy();
}
