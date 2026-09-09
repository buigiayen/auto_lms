using LotusLMS.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LotusLMS.Api;

public static class LotusLmsApiServiceCollectionExtensions
{
    public static IServiceCollection AddLotusLmsApi(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        services
            .AddOptions<LotusLmsOptions>()
            .Bind(configurationSection)
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<LotusLmsOptions>, LotusLmsOptionsValidator>());

        services
            .AddHttpClient<ILotusLmsApiClient, LotusLmsApiClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<LotusLmsOptions>>().Value;

                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", options.AcceptLanguage);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Platform", $"\"{options.BrowserPlatform}\"");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua", "\"Not-A.Brand\";v=\"24\", \"Chromium\";v=\"146\"");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            })
            // Default HttpClientFactory logging includes the complete query string,
            // which contains LotusLMS session context. Add a sanitized logger later if needed.
            .RemoveAllLoggers()
            .UseConfiguredWebProxy()
            // Login cookies and the _sand session must stay on the same handler.
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        return services;
    }
}
