using System.Net;

namespace LotusLMS.Client;

/// <summary>Backend failure without embedding response bodies, API keys or credentials in the exception text.</summary>
public sealed class LotusClientException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string Code { get; }
    public string Endpoint { get; }

    internal LotusClientException(string endpoint, string code, HttpStatusCode? statusCode = null)
        : base($"Client backend request failed ({code}{(statusCode is null ? "" : $", HTTP {(int)statusCode}")}).")
    {
        Endpoint = endpoint.Split('?')[0]; Code = code; StatusCode = statusCode;
    }
}
