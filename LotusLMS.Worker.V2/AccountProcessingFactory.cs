using System.Net;
using LotusLMS.Api;
using LotusLMS.Worker.V2.Accounts;
using LotusLMS.Worker.V2.LotusRunner.CheckingTime;
using LotusLMS.Worker.V2.LotusRunner.Theory;
using Microsoft.Extensions.Options;

namespace LotusLMS.Worker.V2;

internal sealed class AccountProcessingFactory(
    IOptions<LotusLmsOptions> defaultLmsOptions,
    IOptions<WorkerOptions> workerOptions,
    TimeProvider clock,
    ILoggerFactory loggerFactory)
{
    public AccountProcessingSession Create(
        AccountRunContext context,
        AccountWebProxyProvider proxyProvider)
    {
        var defaults = defaultLmsOptions.Value;
        var lmsOptions = context.CreateLmsOptions();
        lmsOptions.RequestTimeoutSeconds = defaults.RequestTimeoutSeconds;
        lmsOptions.DeviceIdentityDirectory = defaults.DeviceIdentityDirectory;
        lmsOptions.GenerateRequestTokens = defaults.GenerateRequestTokens;
        lmsOptions.ApiResponseLogDirectory = defaults.ApiResponseLogDirectory;
        lmsOptions.UserAgent = defaults.UserAgent;
        lmsOptions.BrowserPlatform = defaults.BrowserPlatform;
        lmsOptions.AcceptLanguage = defaults.AcceptLanguage;
        lmsOptions.SendBearerToken = defaults.SendBearerToken;

        var proxy = context.HasProxy ? proxyProvider.GetProxy() : null;
        var handler = new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = proxy is not null,
            AutomaticDecompression = DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(lmsOptions.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(lmsOptions.RequestTimeoutSeconds)
        };
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept", "application/json, text/plain, */*");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", lmsOptions.AcceptLanguage);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", lmsOptions.UserAgent);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Sec-Ch-Ua-Platform", $"\"{lmsOptions.BrowserPlatform}\"");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Sec-Ch-Ua", "\"Not-A.Brand\";v=\"24\", \"Chromium\";v=\"146\"");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");

        var client = new LotusLmsApiClient(httpClient, Options.Create(lmsOptions));
        var learningTime = new LearningTimeTrackingService(
            client,
            loggerFactory.CreateLogger<LearningTimeTrackingService>());
        var practice = new PracticeServices(
            client,
            clock,
            learningTime,
            loggerFactory.CreateLogger<PracticeServices>(),
            workerOptions);
        var theory = new TheoryServices(
            client,
            clock,
            workerOptions,
            loggerFactory.CreateLogger<TheoryServices>());
        var proxyIpChecker = new ProxyIpChecker(proxyProvider);
        var checkingTime = new CheckingTimeService(
            client,
            loggerFactory.CreateLogger<CheckingTimeService>(),
            learningTime,
            practice,
            theory,
            proxyIpChecker);

        return new AccountProcessingSession(httpClient, checkingTime);
    }
}

internal sealed class AccountProcessingSession(
    HttpClient httpClient,
    CheckingTimeService checkingTimeService) : IDisposable
{
    public CheckingTimeService CheckingTimeService => checkingTimeService;

    public void Dispose() => httpClient.Dispose();
}
