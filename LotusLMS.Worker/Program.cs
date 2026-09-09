using LotusLMS.Api;
using LotusLMS.Http;
using LotusLMS.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

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
    .ValidateOnStart();

builder.Services
    .AddOptions<LotusLmsCredentialsOptions>()
    .Bind(builder.Configuration.GetSection(LotusLmsCredentialsOptions.SectionName));

builder.Services.AddWebProxySupport(
    builder.Configuration.GetSection(WebProxyOptions.SectionName));

builder.Services.AddLotusLmsApi(
    builder.Configuration.GetSection(LotusLmsOptions.SectionName));

builder.Services.AddSingleton<LotusLmsReadOnlyProbe>();
builder.Services.AddSingleton<LotusLmsAutoStudy>();
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
await host.RunAsync();
