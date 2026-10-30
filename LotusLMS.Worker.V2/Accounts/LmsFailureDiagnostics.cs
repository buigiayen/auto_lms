using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using LotusLMS.Api;

namespace LotusLMS.Worker.V2.Accounts;

internal static class LmsFailureDiagnostics
{
    public static string Describe(LotusLmsApiException error)
    {
        // Endpoint queries and exception messages can contain session tokens or credentials.
        var endpoint = error.Endpoint.Split('?', '#')[0];
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) endpoint = uri.AbsolutePath;
        var status = error.StatusCode is { } code ? $"HTTP {(int)code}" : "HTTP chưa xác định";
        var cause = error.InnerException;
        string reason;
        if (error.HttpDiagnostics?["failureKind"]?.GetValue<string>() is "application_rejected")
            reason = "LMS từ chối yêu cầu ở mức ứng dụng";
        else if (error.HttpDiagnostics?["failureKind"]?.GetValue<string>() is "invalid_json")
            reason = "phản hồi rỗng hoặc không phải JSON object hợp lệ";
        else if (cause is JsonException json)
            reason = $"dữ liệu JSON không khớp kiểu phản hồi; dòng {json.LineNumber}, vị trí {json.BytePositionInLine}";
        else if (cause is OperationCanceledException)
            reason = "hết thời gian chờ LMS";
        else if (cause is HttpRequestException http)
        {
            reason = $"lỗi kết nối {http.HttpRequestError}";
            for (var inner = http.InnerException; inner is not null; inner = inner.InnerException)
            {
                if (inner is SocketException socket) reason += $"; socket={socket.SocketErrorCode}";
                else if (inner is AuthenticationException) reason += "; TLS=AuthenticationException";
            }
        }
        else
            reason = error.StatusCode is null ? "chưa xác định loại lỗi" : "phản hồi LMS không hợp lệ hoặc bị từ chối";

        var summary = $"Lỗi LMS tại {endpoint} ({status}; {reason}).";
        // Only messages sanitized by the HTTP transport are suitable for shared worker logs.
        if (error.HttpDiagnostics?["message"]?.GetValue<string>() is { Length: > 0 } message)
            summary += $" {nameof(LotusLmsApiException)}: {message}";
        if (error.HttpDiagnostics?["serverMessage"]?.GetValue<string>() is { Length: > 0 } serverMessage)
            summary += $" LMS: {serverMessage}";
        return summary;
    }

    public static Dictionary<string, object?> Context(LotusLmsApiException error) => new()
    {
        ["exceptionType"] = error.GetType().Name,
        ["stackTrace"] = error.StackTrace is { } trace ? trace[..Math.Min(trace.Length, 2048)] : null,
        ["http"] = error.HttpDiagnostics?.DeepClone()
    };
}
