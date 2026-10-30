using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LotusLMS.Api;

// Capture only failure diagnostics. Never retain a raw request body or authentication headers in logs.
internal sealed class ApiFailureDiagnostics
{
    private const string Redacted = "[REDACTED]";
    private static readonly HashSet<string> RequestFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "take_id", "finished", "time_start", "time_used", "score", "paper_score", "practice_iid",
        "course_iid", "exam_sco_iid", "should_save_progress_for_course", "lang", "item_iid",
        "enrolment_plan_iid", "page", "items_per_page", "seconds"
    };
    private const string CredentialKeyPattern = @"token|pass|secret|cookie|authorization|session|device|credential|api.?key|lname|username|_sand_ri";
    private static readonly Regex CredentialKey = new(
        CredentialKeyPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveKey = new(
        CredentialKeyPattern + @"|answer|take.?info|_sand_uid|_sand_uiid",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Assignments = new(
        """(["']?(?:[A-Za-z0-9_.-]{0,64}(?:token|pass(?:word)?|secret|cookie|authorization|session|device|credential|api.?key|answer)[A-Za-z0-9_.-]{0,64}|lname|username|_sand_ri|_sand_rit)["']?\s*[:=]\s*)(?:"[^"]*(?:"|$)|'[^']*(?:'|$)|[^\s,;&}]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AuthorizationValue = new("""(?i)\b(Bearer|Basic)\s+[^\s"',;]+""");
    private static readonly Regex Url = new("""https?://[^\s"'<>]+""", RegexOptions.IgnoreCase);

    private readonly JsonObject _request;
    private readonly string[] _secrets;
    private readonly long _started = Stopwatch.GetTimestamp();
    private JsonObject? _response;
    private JsonNode? _body;
    private string? _rawBody;

    public ApiFailureDiagnostics(HttpRequestMessage request, IEnumerable<KeyValuePair<string, string>>? form = null,
        Uri? baseAddress = null)
    {
        var fields = (form ?? []).Concat(ReadQuery(request.RequestUri)).ToArray();
        _secrets = fields.Where(field => CredentialKey.IsMatch(field.Key) && !string.IsNullOrEmpty(field.Value))
            .SelectMany(field => new[] { field.Value, Uri.EscapeDataString(field.Value), JsonEncodedText.Encode(field.Value).ToString() })
            .Concat(request.Headers.Authorization is { Parameter: { Length: > 0 } token } ? [token] : [])
            .Distinct().OrderByDescending(value => value.Length).ToArray();
        var safeFields = new JsonObject();
        foreach (var field in fields.Where(field => RequestFields.Contains(field.Key)).Take(24))
            safeFields[field.Key] = Clean(field.Value, 128);
        var requestUri = request.RequestUri;
        if (requestUri is { IsAbsoluteUri: false } && baseAddress is not null)
            requestUri = new Uri(baseAddress, requestUri);
        var endpoint = requestUri is { IsAbsoluteUri: true } uri
            ? uri.GetLeftPart(UriPartial.Path)
            : requestUri?.OriginalString.Split('?', '#')[0];
        _request = new JsonObject
        {
            ["method"] = request.Method.Method,
            ["url"] = endpoint is null ? null : Clean(endpoint),
            ["contentType"] = request.Content?.Headers.ContentType?.MediaType,
            ["fields"] = safeFields,
            ["omittedFieldCount"] = fields.Count(field => !RequestFields.Contains(field.Key))
        };
    }

    public void SetResponse(HttpResponseMessage response, string? body = null, JsonNode? root = null)
    {
        _response = new JsonObject
        {
            ["statusCode"] = (int)response.StatusCode,
            ["contentType"] = response.Content.Headers.ContentType?.MediaType,
            ["requestId"] = Header(response, "X-Request-ID"),
            ["correlationId"] = Header(response, "X-Correlation-ID"),
            ["retryAfter"] = Header(response, "Retry-After"),
            ["bodyLength"] = body?.Length
        };
        _rawBody = body;
        _body = root;
    }

    public LotusLmsApiException CreateException(string kind, string message, string endpoint,
        HttpStatusCode? statusCode = null, string? serverMessage = null, Exception? innerException = null)
    {
        var remaining = 4096;
        var response = _response?.DeepClone().AsObject();
        if (response is not null && _rawBody is not null)
            response["body"] = _body is not null
                ? Sanitize(_body, 0, ref remaining)
                : JsonValue.Create(_rawBody.TrimStart().StartsWith('{') || _rawBody.TrimStart().StartsWith('[')
                    ? "[INVALID JSON: body omitted; see parser position in causes]"
                    : Clean(_rawBody, 1024));
        var safeMessage = Clean(message);
        var safeServerMessage = serverMessage is null ? null : Clean(serverMessage);
        var details = new JsonObject
        {
            ["failureKind"] = kind,
            ["exceptionType"] = nameof(LotusLmsApiException),
            ["message"] = safeMessage,
            ["serverMessage"] = safeServerMessage,
            ["durationMs"] = Math.Round(Stopwatch.GetElapsedTime(_started).TotalMilliseconds),
            ["request"] = _request.DeepClone(),
            ["response"] = response
        };
        var causes = new JsonArray();
        for (var cause = innerException; cause is not null && causes.Count < 4; cause = cause.InnerException)
        {
            var entry = new JsonObject { ["type"] = cause.GetType().Name, ["message"] = Clean(cause.Message) };
            if (cause is JsonException json)
            {
                entry["jsonPath"] = json.Path is null ? null : Clean(json.Path);
                entry["lineNumber"] = json.LineNumber;
                entry["bytePositionInLine"] = json.BytePositionInLine;
            }
            if (cause is HttpRequestException http) entry["httpRequestError"] = http.HttpRequestError.ToString();
            causes.Add(entry);
        }
        details["causes"] = causes;
        return new LotusLmsApiException(safeMessage, endpoint, statusCode, safeServerMessage, innerException, details);
    }

    private JsonNode? Sanitize(JsonNode? node, int depth, ref int remaining)
    {
        if (remaining <= 0) return JsonValue.Create("[TRUNCATED]");
        remaining -= 4;
        if (node is null) return null;
        if (depth > 5)
        {
            remaining -= 16;
            return JsonValue.Create("[TRUNCATED]");
        }
        if (node is JsonObject source)
        {
            var result = new JsonObject();
            foreach (var field in source.Take(16))
            {
                if (remaining <= 0) break;
                var key = Clean(field.Key, 80);
                remaining -= key.Length + 8;
                if (SensitiveKey.IsMatch(field.Key))
                {
                    remaining -= Redacted.Length;
                    result[key] = Redacted;
                }
                else result[key] = Sanitize(field.Value, depth + 1, ref remaining);
            }
            if (source.Count > result.Count) result["_truncated"] = true;
            return result;
        }
        if (node is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var child in array.Take(10))
            {
                if (remaining <= 0) break;
                result.Add(Sanitize(child, depth + 1, ref remaining));
            }
            if (array.Count > result.Count) result.Add("[TRUNCATED]");
            return result;
        }
        var text = node is JsonValue value && value.TryGetValue<string>(out var scalar) ? scalar : node.ToJsonString();
        var clean = Clean(text, Math.Clamp(remaining, 1, 512));
        remaining -= clean.Length + 4;
        return node is JsonValue original && !original.TryGetValue<string>(out _) && clean == text
            ? node.DeepClone() : JsonValue.Create(clean);
    }

    private string Clean(string value, int limit = 512)
    {
        foreach (var secret in _secrets) value = value.Replace(secret, Redacted, StringComparison.Ordinal);
        // Redact known values before clipping, so a secret crossing the preview boundary cannot leak.
        if (value.Length > limit + 512) value = value[..(limit + 512)] + "…[TRUNCATED]";
        value = AuthorizationValue.Replace(value, "$1 " + Redacted);
        value = Assignments.Replace(value, "$1" + Redacted);
        value = Url.Replace(value, match =>
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return "[URL REDACTED]";
            return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Path);
        });
        value = string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));
        return value.Length <= limit ? value : value[..limit] + "…[TRUNCATED]";
    }

    private string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? Clean(string.Join(", ", values), 128) : null;

    private static IEnumerable<KeyValuePair<string, string>> ReadQuery(Uri? uri)
    {
        var query = uri is { IsAbsoluteUri: true } ? uri.Query.TrimStart('?') : uri?.OriginalString.Split('?', 2).ElementAtOrDefault(1);
        foreach (var part in (query ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            yield return new(Uri.UnescapeDataString(pair[0].Replace('+', ' ')), Uri.UnescapeDataString(pair.ElementAtOrDefault(1)?.Replace('+', ' ') ?? ""));
        }
    }
}
