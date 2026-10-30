using System.Net;
using System.Text.Json.Nodes;

namespace LotusLMS.Api;

public sealed class LotusLmsApiException : Exception
{
    public LotusLmsApiException(
        string message,
        string endpoint,
        HttpStatusCode? statusCode = null,
        string? serverMessage = null,
        Exception? innerException = null,
        JsonObject? httpDiagnostics = null)
        : base(message, innerException)
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
        ServerMessage = serverMessage;
        HttpDiagnostics = httpDiagnostics;
    }

    public string Endpoint { get; }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// A short message field returned by LotusLMS. Full response bodies are intentionally excluded.
    /// </summary>
    public string? ServerMessage { get; }

    /// <summary>Bounded HTTP failure details with authentication and answer data removed.</summary>
    public JsonObject? HttpDiagnostics { get; }
}
