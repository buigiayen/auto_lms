using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LotusLMS.Api;

internal sealed class ApiResponseFileWriter(LotusLmsOptions options)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly bool _enabled = options.LogApiResponses;
    private readonly string _directory = string.IsNullOrWhiteSpace(options.ApiResponseLogDirectory)
        ? string.Empty
        : Path.GetFullPath(options.ApiResponseLogDirectory, Directory.GetCurrentDirectory());

    public async Task WriteAsync(
        HttpMethod method,
        Uri? requestUri,
        HttpResponseMessage response,
        string body)
    {
        if (!_enabled)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        SetOwnerOnlyDirectoryPermissions(_directory);

        var uriPath = ReadUriPath(requestUri);
        var filePath = Path.Combine(_directory, BuildFileName(uriPath));
        var fileLock = FileLocks.GetOrAdd(filePath, static _ => new SemaphoreSlim(1, 1));

        await fileLock.WaitAsync(CancellationToken.None);
        try
        {
            var record = new JsonObject
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["method"] = method.Method,
                ["uri"] = uriPath,
                ["statusCode"] = (int)response.StatusCode,
                ["reasonPhrase"] = response.ReasonPhrase,
                ["headers"] = BuildHeaders(response.Headers),
                ["contentHeaders"] = BuildHeaders(response.Content.Headers),
                ["body"] = ParseBody(body)
            };

            if (!File.Exists(filePath))
            {
                await using var newFile = new FileStream(
                    filePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 1,
                    useAsync: true);
            }

            SetOwnerOnlyFilePermissions(filePath);

            await File.AppendAllTextAsync(
                filePath,
                record.ToJsonString(SerializerOptions) + Environment.NewLine,
                Encoding.UTF8,
                CancellationToken.None);
        }
        finally
        {
            fileLock.Release();
        }
    }

    private static string ReadUriPath(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return "/unknown";
        }

        var path = requestUri.IsAbsoluteUri
            ? requestUri.AbsolutePath
            : requestUri.OriginalString.Split('?', 2)[0];

        path = Uri.UnescapeDataString(path).Trim();
        return "/" + path.TrimStart('/');
    }

    private static string BuildFileName(string uriPath)
    {
        var normalized = Regex.Replace(uriPath.Trim('/'), @"[^A-Za-z0-9.-]+", "_")
            .Trim('_', '.');
        return (string.IsNullOrEmpty(normalized) ? "root" : normalized) + ".jsonl";
    }

    private static JsonObject BuildHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var result = new JsonObject();
        foreach (var header in headers)
        {
            result[header.Key] = new JsonArray(
                header.Value.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray());
        }

        return result;
    }

    private static JsonNode? ParseBody(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return JsonValue.Create(body);
        }
    }

    private static void SetOwnerOnlyDirectoryPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void SetOwnerOnlyFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
