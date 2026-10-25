using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LotusLMS.Client;

public static class LotusClientServiceCollectionExtensions
{
    /// <summary>Register a typed client with direct transport and redirects/loggers disabled.</summary>
    public static IServiceCollection AddLotusClient(this IServiceCollection services, IConfigurationSection configuration)
    {
        services.AddOptions<LotusClientOptions>().Bind(configuration)
            .Validate(options => options.IsValid(), "Client requires an HTTP(S) BaseUrl without credentials/query/fragment and RequestTimeoutSeconds from 1 to 300.")
            .ValidateOnStart();
        services.AddHttpClient<ILotusClient, LotusClient>()
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
        return services;
    }
}
