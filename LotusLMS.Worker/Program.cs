using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Http;
using LotusLMS.Worker;
using Microsoft.Extensions.Options;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

Console.WriteLine($"=== {WebProxyOptions.SectionName} configuration ===");

PrintConfigurationSection(
    builder.Configuration.GetSection(WebProxyOptions.SectionName),
    WebProxyOptions.SectionName);


Console.WriteLine($"=== {WorkerOptions.SectionName} configuration ===");

PrintConfigurationSection(
    builder.Configuration.GetSection(WorkerOptions.SectionName),
    WorkerOptions.SectionName);


Console.WriteLine($"=== {LotusLmsCredentialsOptions.SectionName} configuration ===");
PrintConfigurationSection(
    builder.Configuration.GetSection(LotusLmsCredentialsOptions.SectionName),
    LotusLmsCredentialsOptions.SectionName);


Console.WriteLine($"=== {LotusLmsOptions.SectionName} configuration ===");
PrintConfigurationSection(
    builder.Configuration.GetSection(LotusLmsOptions.SectionName),
    LotusLmsOptions.SectionName);

Console.WriteLine($"=== configuration ===");

builder.Services
    .AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .Validate(
        options => options.StatusIntervalSeconds is >= 5 and <= 3600,
        $"{WorkerOptions.SectionName}:StatusIntervalSeconds must be between 5 and 3600 seconds.")
    .Validate(
        options => options.PlanPageSize is >= 1 and <= 200,
        $"{WorkerOptions.SectionName}:PlanPageSize must be between 1 and 200.")
    .Validate(
        options => options.CoursePageSize is >= 1 and <= 200,
        $"{WorkerOptions.SectionName}:CoursePageSize must be between 1 and 200.")
    .Validate(
        options => options.MaxPages is >= 1 and <= 1000,
        $"{WorkerOptions.SectionName}:MaxPages must be between 1 and 1000.")
    .Validate(
        options => options.SaveItemSeconds is >= 1 and <= 15,
        $"{WorkerOptions.SectionName}:SaveItemSeconds must be between 1 and 15.")
    .Validate(
        options => options.TrackTimeSeconds is >= 1 and <= 60,
        $"{WorkerOptions.SectionName}:TrackTimeSeconds must be between 1 and 60.")
    .Validate(
        options => options.KpiCheckEverySaves is >= 1 and <= 100,
        $"{WorkerOptions.SectionName}:KpiCheckEverySaves must be between 1 and 100.")
    .Validate(
        options => options.PracticeBatchSize is >= 1 and <= 50,
        $"{WorkerOptions.SectionName}:PracticeBatchSize must be between 1 and 50.")
    .Validate(
        options => options.PracticeIntervalSeconds is >= 1 and <= 3600,
        $"{WorkerOptions.SectionName}:PracticeIntervalSeconds must be between 1 and 3600.")
    .Validate(
        options => options.PracticeMaxIntervalSeconds >= options.PracticeIntervalSeconds
            && options.PracticeMaxIntervalSeconds <= 3600,
        $"{WorkerOptions.SectionName}:PracticeMaxIntervalSeconds must be at least PracticeIntervalSeconds and no greater than 3600.")
    .Validate(
        options => options.Mode != WorkerMode.PracticeOnly
            || (options.PracticeTargets.Length > 0 && options.PracticeTargets.All(target => target.IsValid)),
        $"{WorkerOptions.SectionName}:PracticeTargets must contain at least one target with positive CourseIid and PracticeIid in PracticeOnly mode. Configure Worker:PracticeTargets or use Worker:Mode=Idle.")
    .ValidateOnStart();

builder.Services
    .AddOptions<LotusLmsCredentialsOptions>()
    .Bind(builder.Configuration.GetSection(LotusLmsCredentialsOptions.SectionName));

builder.Services.AddWebProxySupport(
    builder.Configuration.GetSection(WebProxyOptions.SectionName));

builder.Services.AddLotusLmsApi(
    builder.Configuration.GetSection(LotusLmsOptions.SectionName));

builder.Services.AddSingleton<LotusLmsReadOnlyProbe>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LotusLmsAccountReader>();
builder.Services.AddSingleton<LotusLmsPractice>();
builder.Services.AddSingleton<LotusLmsPracticeOnly>();
builder.Services.AddSingleton<LotusLmsAutoStudy>();
builder.Services.AddOptions<ClientWorkerOptions>()
    .Bind(builder.Configuration.GetSection(ClientWorkerOptions.SectionName))
    .Validate(options => !options.Enabled || (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && (string.IsNullOrEmpty(options.AccountId) || Guid.TryParse(options.AccountId, out _))
        && !string.IsNullOrWhiteSpace(options.WorkerName) && !string.IsNullOrWhiteSpace(options.ApiKey)),
        "Client requires an HTTP(S) BaseUrl, WorkerName, ApiKey and an optional valid AccountId when enabled.")
    .ValidateOnStart();
builder.Services.AddLotusClient(builder.Configuration.GetSection(LotusClientOptions.SectionName));
builder.Services.AddSingleton<ClientWorkerBridge>();
builder.Services.AddSingleton<ClientAccountProxyProvider>();
builder.Services.AddSingleton<IWebProxyProvider>(services => services.GetRequiredService<ClientAccountProxyProvider>());
builder.Services.AddTransient<ClientLeaseHandler>();
builder.Services.AddHttpClient<ILotusLmsApiClient, LotusLmsApiClient>().AddHttpMessageHandler<ClientLeaseHandler>();
builder.Services.AddSingleton<ILoggerProvider>(services => services.GetRequiredService<ClientWorkerBridge>());
builder.Services.AddHostedService(services => services.GetRequiredService<ClientWorkerBridge>());
builder.Services.AddHostedService<Worker>();

try
{
    using IHost host = builder.Build();
    await host.RunAsync();
}
catch (OptionsValidationException exception)
{
    Console.Error.WriteLine("Cannot start LotusLMS worker because configuration is invalid:");
    foreach (var failure in exception.Failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.ExitCode = 1;
}

static void PrintConfigurationSection(IConfiguration section, string path)
{
    foreach (var child in section.GetChildren())
    {
        var childPath = $"{path}:{child.Key}";
        if (child.GetChildren().Any())
        {
            PrintConfigurationSection(child, childPath);
        }
        else
        {
            var sensitive = child.Key.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || child.Key.Contains("Token", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"{childPath} = {(sensitive ? "***" : child.Value)}");
        }
    }
}
