using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using LotusLMS.Worker.V2.Accounts;

namespace LotusLMS.Worker.V2;

internal sealed class GreenConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "green";

    public override void Write<TState>(in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null) return;

        textWriter.Write("\u001b[92m");
        textWriter.Write($"[{DateTimeOffset.Now:HH:mm:ss.fff}] [{logEntry.LogLevel}] ");
        scopeProvider?.ForEachScope((scope, writer) =>
        {
            if (scope is AccountLogScope account) writer.Write(account.ToString());
        }, textWriter);
        textWriter.Write($"{logEntry.Category}: ");
        textWriter.Write(message);
        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine();
            textWriter.Write(logEntry.Exception);
        }
        textWriter.Write("\u001b[0m");
        textWriter.WriteLine();
    }
}
