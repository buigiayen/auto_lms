using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Http;
using LotusLMS.Worker.V2;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsoleFormatter<GreenConsoleFormatter, ConsoleFormatterOptions>();
builder.Logging.AddConsole(options => options.FormatterName = GreenConsoleFormatter.FormatterName);

builder.Services.AddLotusLmsApi(builder.Configuration.GetSection(LotusLmsOptions.SectionName));
builder.Services.AddOptions<LotusClientOptions>()
    .Bind(builder.Configuration.GetSection(LotusClientOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.UserInfo.Length == 0
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
        && options.RequestTimeoutSeconds is >= 1 and <= 300,
        "Client:BaseUrl phải là HTTP(S) hợp lệ và Client:RequestTimeoutSeconds phải từ 1 đến 300.")
    .ValidateOnStart();
builder.Services.AddOptions<WorkerOptions>().Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<Worker>();


try
{
    using var host = builder.Build();
    await host.RunAsync();
}
catch (OptionsValidationException error)
{
    ReportConfigurationErrors([error]);
}
catch (AggregateException error) when (error.Flatten().InnerExceptions.All(exception => exception is OptionsValidationException))
{
    ReportConfigurationErrors(error.Flatten().InnerExceptions.Cast<OptionsValidationException>());
}

static void ReportConfigurationErrors(IEnumerable<OptionsValidationException> errors)
{
    Console.Error.WriteLine("Cấu hình Worker V2 chưa hợp lệ:");
    foreach (var failure in errors.SelectMany(error => error.Failures).Distinct())
        Console.Error.WriteLine($"- {failure}");
    Console.Error.WriteLine("Kiểm tra appsettings, User Secrets hoặc biến môi trường theo README của Worker V2.");
    Environment.ExitCode = 1;
}
