using System.Net;

namespace LotusLMS.Api;

public sealed class LotusLmsApiException : Exception
{
    public LotusLmsApiException(
        string message,
        string endpoint,
        HttpStatusCode? statusCode = null,
        string? serverMessage = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public string Endpoint { get; }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// A short message field returned by LotusLMS. Full response bodies are intentionally excluded.
    /// </summary>
    public string? ServerMessage { get; }
}
