using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LotusLMS.RequestSigning;

namespace LotusLMS.Api;

/// <summary>
/// Owns the HTTP mechanics for LotusLMS: request creation, common headers,
/// transport errors, response logging, JSON validation and deserialization.
/// Business endpoints and payload construction stay in LotusLmsApiClient.
/// </summary>
internal sealed class LotusLmsApiTransport(
    HttpClient httpClient,
    LotusLmsOptions options)
{
    private static readonly JsonSerializerOptions ResponseSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new FlexibleBooleanJsonConverter() }
    };

    private readonly ApiResponseFileWriter _responseFileWriter = new(options);

    public async Task<JsonObject> PostAsync(
        string endpoint,
        IEnumerable<KeyValuePair<string, string>> form,
        string referer,
        LotusLmsSession? session,
        CancellationToken cancellationToken,
        LotusLmsProvider? provider = null)
    {
        provider ??= session?.Provider;
        using var request = CreatePostRequest(endpoint, form, referer, session, provider);
        var (_, root, _) = await SendAsync(request, endpoint, cancellationToken);
        return root;
    }

    public async Task<TResponse> PostAsync<TResponse>(
        string endpoint,
        IEnumerable<KeyValuePair<string, string>> form,
        string referer,
        LotusLmsSession? session,
        CancellationToken cancellationToken,
        LotusLmsProvider? provider = null)
        where TResponse : class
    {
        provider ??= session?.Provider;
        using var request = CreatePostRequest(endpoint, form, referer, session, provider);
        var (body, _, statusCode) = await SendAsync(request, endpoint, cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<TResponse>(body, ResponseSerializerOptions)
                ?? throw InvalidJsonResponse(endpoint, statusCode);
        }
        catch (JsonException exception)
        {
            throw new LotusLmsApiException(
                $"LotusLMS response could not be deserialized as {typeof(TResponse).Name} " +
                $"at JSON path {exception.Path ?? "$"}.",
                endpoint,
                statusCode,
                innerException: exception);
        }
    }

    public async Task<JsonObject> PostMultipartAsync(
        string endpoint,
        IEnumerable<KeyValuePair<string, string>> form,
        string referer,
        LotusLmsSession session,
        CancellationToken cancellationToken)
    {
        var provider = session.Provider;
        using var content = new MultipartFormDataContent();
        var fields = options.GenerateRequestTokens
            ? SandRequestTokenGenerator.Sign(form, session.UserIid, session.Token)
            : form;
        foreach (var field in fields)
        {
            content.Add(new StringContent(field.Value, System.Text.Encoding.UTF8), field.Key);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildRequestUri(endpoint, provider)) { Content = content };
        ApplyRequestHeaders(request, referer, session, provider);
        var (_, root, _) = await SendAsync(request, endpoint, cancellationToken);
        return root;
    }

    public async Task<JsonObject> GetAsync(
        string endpoint,
        string referer,
        LotusLmsSession session,
        CancellationToken cancellationToken)
    {
        var signedEndpoint = endpoint;
        if (options.GenerateRequestTokens)
        {
            var tokens = SandRequestTokenGenerator.Generate(session.UserIid);
            signedEndpoint += (endpoint.Contains('?') ? "&" : "?")
                + SandRequestTokenGenerator.RequestIdField + "=" + Uri.EscapeDataString(tokens.RequestId)
                + "&" + SandRequestTokenGenerator.RequestTokenField + "=" + Uri.EscapeDataString(tokens.RequestToken)
                + "&" + SandClientSyncTokenGenerator.FieldName + "="
                + Uri.EscapeDataString(SandClientSyncTokenGenerator.Generate(session.Token))
                + "&_sand_token=" + Uri.EscapeDataString(session.Token)
                + "&_sand_uiid=" + session.UserIid.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "&_sand_uid=" + Uri.EscapeDataString(session.UserId);
        }

        var provider = session.Provider;
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(signedEndpoint, provider));
        ApplyRequestHeaders(request, referer, session, provider);
        var (_, root, _) = await SendAsync(request, endpoint, cancellationToken);
        return root;
    }

    private HttpRequestMessage CreatePostRequest(
        string endpoint,
        IEnumerable<KeyValuePair<string, string>> form,
        string referer,
        LotusLmsSession? session,
        LotusLmsProvider? provider)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildRequestUri(endpoint, provider))
        {
            Content = new FormUrlEncodedContent(options.GenerateRequestTokens
                ? SandRequestTokenGenerator.Sign(form, session?.UserIid, session?.Token)
                : form)
        };
        ApplyRequestHeaders(request, referer, session, provider);
        return request;
    }

    private Uri BuildRequestUri(string endpoint, LotusLmsProvider? provider)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        return provider is null
            ? new Uri(endpoint, UriKind.Relative)
            : new Uri(provider.BaseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/'), UriKind.Absolute);
    }

    private async Task<(string Body, JsonObject Root, System.Net.HttpStatusCode StatusCode)> SendAsync(
        HttpRequestMessage request,
        string endpoint,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LotusLmsApiException(
                "The LotusLMS request timed out.",
                endpoint,
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new LotusLmsApiException(
                "The LotusLMS request failed before a valid response was received.",
                endpoint,
                exception.StatusCode,
                innerException: exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            await _responseFileWriter.WriteAsync(
                request.Method,
                request.RequestUri,
                response,
                body);

            var root = TryParseObject(body);
            var serverMessage = ReadServerMessage(root);

            if (!response.IsSuccessStatusCode)
            {
                throw new LotusLmsApiException(
                    $"LotusLMS returned HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    endpoint,
                    response.StatusCode,
                    serverMessage);
            }

            if (root is null)
            {
                throw InvalidJsonResponse(endpoint, response.StatusCode);
            }

            if (TryReadBoolean(root["success"], out var success) && !success)
            {
                throw new LotusLmsApiException(
                    "LotusLMS rejected the request at the application level.",
                    endpoint,
                    response.StatusCode,
                    serverMessage);
            }

            return (body, root, response.StatusCode);
        }
    }

    private void ApplyRequestHeaders(
        HttpRequestMessage request,
        string referer,
        LotusLmsSession? session,
        LotusLmsProvider? provider)
    {
        request.Headers.Referrer = new Uri(referer, UriKind.Absolute);
        request.Headers.TryAddWithoutValidation(
            "Origin",
            (provider?.WebOrigin ?? options.WebOrigin).TrimEnd('/'));

        if (options.SendBearerToken && session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        }
    }

    private static JsonObject? TryParseObject(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LotusLmsApiException InvalidJsonResponse(
        string endpoint,
        System.Net.HttpStatusCode? statusCode = null) =>
        new(
            "LotusLMS returned an empty or invalid JSON object.",
            endpoint,
            statusCode);

    private static string? ReadServerMessage(JsonObject? root)
    {
        if (root is null)
        {
            return null;
        }

        var message = ReadString(root["message"])
            ?? ReadString(root["error"])
            ?? ReadString(root["error_message"])
            ?? ReadString(root["err_code"])
            ?? ReadString(root["error_code"]);

        return message is { Length: > 512 } ? message[..512] : message;
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        var scalar = value.ToString();
        return string.IsNullOrWhiteSpace(scalar) ? null : scalar;
    }

    private static bool TryReadBoolean(JsonNode? node, out bool value)
    {
        value = false;
        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<bool>(out value))
        {
            return true;
        }

        if (jsonValue.TryGetValue<int>(out var number))
        {
            value = number != 0;
            return true;
        }

        var text = ReadString(node);
        if (bool.TryParse(text, out value))
        {
            return true;
        }

        if (int.TryParse(text, out number))
        {
            value = number != 0;
            return true;
        }

        return false;
    }

    private sealed class FlexibleBooleanJsonConverter : JsonConverter<bool>
    {
        public override bool Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions serializerOptions)
        {
            if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
            {
                return reader.GetBoolean();
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                return number != 0;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                if (bool.TryParse(text, out var boolean))
                {
                    return boolean;
                }

                if (int.TryParse(text, out number))
                {
                    return number != 0;
                }
            }

            throw new JsonException("Expected a boolean, 0/1 number, or boolean string.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            bool value,
            JsonSerializerOptions serializerOptions) =>
            writer.WriteBooleanValue(value);
    }
}
