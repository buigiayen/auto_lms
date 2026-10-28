using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LotusLMS.Http;

public static class WebProxyServiceCollectionExtensions
{
    /// <summary>
    /// Registers proxy configuration and the configured proxy provider.
    /// </summary>
    public static IServiceCollection AddWebProxySupport(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        services
            .AddOptions<WebProxyOptions>()
            .Bind(configurationSection)
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<WebProxyOptions>, WebProxyOptionsValidator>());
        services.TryAddSingleton<IWebProxyProvider, ConfiguredWebProxyProvider>();

        return services;
    }
}
