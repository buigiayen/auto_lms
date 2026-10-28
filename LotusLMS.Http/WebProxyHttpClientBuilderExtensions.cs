using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace LotusLMS.Http;

public static class WebProxyHttpClientBuilderExtensions
{
    private static readonly TimeSpan DefaultPooledConnectionLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Configures a sockets handler with the registered proxy, cookie support,
    /// response decompression, and connection recycling.
    /// </summary>
    public static IHttpClientBuilder UseConfiguredWebProxy(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
        {
            var proxy = serviceProvider
                .GetRequiredService<IWebProxyProvider>()
                .GetProxy();

            return new SocketsHttpHandler
            {
                Proxy = proxy,
                UseProxy = proxy is not null,
                AutomaticDecompression = DecompressionMethods.GZip
                                         | DecompressionMethods.Deflate
                                         | DecompressionMethods.Brotli,
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                PooledConnectionLifetime = DefaultPooledConnectionLifetime
            };
        });
    }
}
