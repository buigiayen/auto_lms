using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using LotusLMS.Worker.V2.Accounts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class HttpFailureChecks
{
    public static async Task RunAsync(LotusLmsSession session, Action<bool, string> check)
    {
        var rejectedBody = new JsonObject
        {
            ["success"] = false,
            ["message"] = "Lượt ôn luyện đã đóng.",
            ["error_code"] = "TAKE_CLOSED",
            ["error"] = new JsonObject { ["type"] = "PracticeValidationException", ["field"] = "take_id" },
            ["debug"] = new JsonObject
            {
                ["token"] = "response-only-token",
                ["nested"] = new JsonArray(new JsonObject { ["password"] = "response-only-password" }),
                ["echo"] = $"Session {session.Token}; device {session.DeviceUuid}; browser {session.SessionId}",
                ["detail"] = "password=unrelated-secret; Authorization: Bearer unrelated-bearer; Authorization: Basic unrelated-basic",
                ["url"] = "https://proxy-user:proxy-secret@lms.invalid/save?token=query-secret",
                ["answers"] = new JsonArray("private-answer")
            }
        };
        LotusLmsApiException rejection;
        using (var handler = new Fixture(async (request, _) =>
        {
            var posted = await request.Content!.ReadAsStringAsync();
            check(posted.Contains("take_info[0][iid]") && posted.Contains(session.Token),
                "failure diagnostics preserve the authenticated multipart request sent to LMS");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(rejectedBody.ToJsonString()) };
            response.Headers.Add("X-Request-ID", "lms-request-123");
            response.Headers.Add("Set-Cookie", "session=response-cookie");
            return response;
        }))
            rejection = await RejectSaveAsync(handler, session);

        var details = rejection.HttpDiagnostics!;
        var diagnostic = LmsFailureDiagnostics.Describe(rejection);
        check(rejection.StatusCode == HttpStatusCode.OK && details["failureKind"]!.GetValue<string>() == "application_rejected"
            && diagnostic.Contains("Lượt ôn luyện đã đóng.") && diagnostic.Contains(nameof(LotusLmsApiException)),
            "HTTP 200 application errors expose the actual LMS message and exception instead of a generic failure");
        check(details["request"]!["method"]!.GetValue<string>() == "POST"
            && details["request"]!["url"]!.GetValue<string>() == "https://lms.invalid/practice/api/save-take"
            && details["request"]!["contentType"]!.GetValue<string>() == "multipart/form-data"
            && details["request"]!["fields"]!["course_iid"]!.GetValue<string>() == "110"
            && details["request"]!["fields"]!["finished"]!.GetValue<string>() == "0"
            && details["response"]!["body"]!["error_code"]!.GetValue<string>() == "TAKE_CLOSED"
            && details["response"]!["requestId"]!.GetValue<string>() == "lms-request-123",
            "HTTP diagnostics retain method, URL, multipart fields, response code and correlation ID");
        var serialized = JsonSerializer.Serialize(LmsFailureDiagnostics.Context(rejection));
        foreach (var secret in new[] { session.Token, session.DeviceUuid, session.SessionId, "response-only-token",
            "response-only-password", "unrelated-secret", "unrelated-bearer", "unrelated-basic", "proxy-user", "proxy-secret",
            "query-secret", "private-answer", "response-cookie" })
            check(!serialized.Contains(secret) && !diagnostic.Contains(secret), $"HTTP failure logs redact {secret}");

        // The UI already expands context; verify that the worker delivers the complete safe diagnostic there,
        // including when the client API temporarily rejects the first log request.
        var entries = new List<JsonObject>();
        using (var handler = new Fixture(async (request, _) =>
        {
            entries.Add(await Fixture.Body(request));
            return entries.Count == 1 ? Fixture.Error("TEMPORARY", HttpStatusCode.ServiceUnavailable)
                : Fixture.Json(new { data = new { id = 1 } });
        }))
        using (var client = handler.Client())
        using (var context = Fixture.Context())
        {
            var logs = new AccountLog(client, TimeProvider.System, NullLogger<AccountLog>.Instance);
            await logs.SendAsync(context, "error", diagnostic, default, LmsFailureDiagnostics.Context(rejection));
            check(entries.Count == 2 && JsonNode.DeepEquals(entries[0], entries[1])
                && entries[0]["context"]!["failure"]!["http"]!["response"]!["body"]!["error_code"]!.GetValue<string>() == "TAKE_CLOSED",
                "expanded event data contains the HTTP failure and remains identical across log retries");
            check(Encoding.UTF8.GetByteCount(entries[0].ToJsonString()) < 65536,
                "the complete failure event fits the Client API request limit");
        }

        foreach (var (status, body, expectedKind) in new[]
        {
            (HttpStatusCode.OK, """{"message":"invalid JSON", "token":"malformed-secret", """, "invalid_json"),
            (HttpStatusCode.OK, """{"answers":["private-array-answer","private-second-answer"], "unfinished": """, "invalid_json"),
            (HttpStatusCode.OK, """[{"token":"array-secret","answers":["private-array-answer","private-second-answer"]}]""", "invalid_json"),
            (HttpStatusCode.BadGateway, "<html>Bad Gateway token=html-secret</html>", "http_status")
        })
        {
            using var handler = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));
            var error = await RejectSaveAsync(handler, session);
            var json = error.HttpDiagnostics!.ToJsonString();
            check(error.StatusCode == status && error.HttpDiagnostics["failureKind"]!.GetValue<string>() == expectedKind,
                "malformed JSON and gateway errors preserve their actual HTTP status and failure kind");
            check(!json.Contains("malformed-secret") && !json.Contains("array-secret") && !json.Contains("html-secret")
                && !json.Contains("private-array-answer") && !json.Contains("private-second-answer")
                && error.HttpDiagnostics["response"]!["body"] is not null,
                "non-object and non-JSON response previews remain useful without exposing credentials");
            if (status == HttpStatusCode.OK && body.StartsWith('{'))
                check(error.InnerException is JsonException
                    && error.HttpDiagnostics["causes"]![0]!["type"]!.GetValue<string>() == error.InnerException.GetType().Name
                    && error.HttpDiagnostics["causes"]![0]!["bytePositionInLine"] is not null,
                    "invalid JSON retains the parser exception and position");
        }

        using (var handler = new Fixture((_, _) => Task.FromResult(Fixture.Json(new
        {
            success = false, error = new { message = "Take was rejected", code = "INVALID_TAKE" }
        }))))
        {
            var error = await RejectSaveAsync(handler, session);
            check(LmsFailureDiagnostics.Describe(error).Contains("Take was rejected"),
                "nested LMS error messages are included in the visible failure summary");
        }

        using (var handler = new Fixture((_, _) => Task.FromResult(Fixture.Json(new
        {
            success = false, message = new string('x', 500) + session.Token + " rejected"
        }))))
        {
            var error = await RejectSaveAsync(handler, session);
            check(!LmsFailureDiagnostics.Describe(error).Contains(session.Token[..8]),
                "a session token crossing the server-message length boundary is redacted before truncation");
        }

        using (var handler = new Fixture((_, _) => throw new HttpRequestException(
            HttpRequestError.NameResolutionError, $"DNS failed for https://lms.invalid/save?token={session.Token}")))
        {
            var error = await RejectSaveAsync(handler, session);
            check(error.HttpDiagnostics!["failureKind"]!.GetValue<string>() == "network"
                && error.HttpDiagnostics["response"] is null
                && error.HttpDiagnostics["causes"]![0]!["httpRequestError"]!.GetValue<string>() == "NameResolutionError"
                && !error.HttpDiagnostics.ToJsonString().Contains(session.Token),
                "network failures keep their cause without inventing an HTTP response or exposing request tokens");
        }

        using (var handler = new Fixture((_, _) => Task.FromResult(Fixture.Json(new
        {
            success = false, message = new string('ỗ', 10000),
            debug = Enumerable.Range(0, 50).ToDictionary(number => "field" + number, _ => new string('ỗ', 10000))
        }))))
        {
            var error = await RejectSaveAsync(handler, session);
            var json = JsonSerializer.Serialize(LmsFailureDiagnostics.Context(error));
            check(Encoding.UTF8.GetByteCount(json) < 49152 && json.Contains("TRUNCATED"),
                "large Unicode responses are bounded before reaching terminal or Client API logs");
        }

        JsonArray NestedArray(int depth) => new(Enumerable.Range(0, 4)
            .Select(_ => depth == 0 ? null : (JsonNode?)NestedArray(depth - 1)).ToArray());
        using (var handler = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject { ["success"] = false, ["debug"] = NestedArray(6) }.ToJsonString())
        })))
        {
            var error = await RejectSaveAsync(handler, session);
            var json = JsonSerializer.Serialize(LmsFailureDiagnostics.Context(error));
            check(Encoding.UTF8.GetByteCount(json) < 49152 && json.Contains("TRUNCATED"),
                "nested arrays and null values share the response size budget");
        }

        using (var handler = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new BrokenResponseContent(session.Token) })))
        {
            var error = await RejectSaveAsync(handler, session);
            check(error.StatusCode == HttpStatusCode.OK
                && error.HttpDiagnostics!["failureKind"]!.GetValue<string>() == "response_read"
                && error.HttpDiagnostics["causes"]!.AsArray().Count > 0
                && !error.HttpDiagnostics.ToJsonString().Contains(session.Token),
                "response-stream failures retain the received HTTP status and sanitized inner exception");
        }

        using (var canceled = new CancellationTokenSource())
        using (var handler = new Fixture((_, token) => Task.FromCanceled<HttpResponseMessage>(token)))
        {
            canceled.Cancel();
            try
            {
                await RejectSaveAsync(handler, session, canceled.Token);
                throw new Exception("FAIL: canceled request should be canceled");
            }
            catch (OperationCanceledException)
            {
                check(true, "caller cancellation remains cancellation rather than an LMS failure");
            }
        }

        using (var handler = new Fixture((_, _) => Task.FromResult(Fixture.Json(new { success = false, message = "Denied" }))))
        using (var http = new HttpClient(handler) { BaseAddress = new Uri("https://lms.invalid") })
        {
            var api = new LotusLmsApiClient(http, Options.Create(new LotusLmsOptions { LogApiResponses = false }));
            try
            {
                await api.GetStudentEnrolmentPlansAsync(session);
                throw new Exception("FAIL: enrolment-plan fixture should fail");
            }
            catch (LotusLmsApiException error)
            {
                check(error.HttpDiagnostics!["request"]!["method"]!.GetValue<string>() == "POST"
                    && !error.HttpDiagnostics["request"]!["url"]!.GetValue<string>().Contains('?')
                    && !error.HttpDiagnostics.ToJsonString().Contains(session.Token),
                    "failures with signed URL parameters retain the actual POST method and remove session queries from diagnostics");
            }
        }
    }

    private static async Task<LotusLmsApiException> RejectSaveAsync(HttpMessageHandler handler, LotusLmsSession session,
        CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://lms.invalid") };
        var api = new LotusLmsApiClient(http, Options.Create(new LotusLmsOptions { LogApiResponses = false }));
        try
        {
            await api.SavePracticeTakeAsync(session, new SavePracticeTakeRequest
            {
                PracticeIid = 22, ExamScoIid = 33, CourseIid = 110, TakeId = "take-456",
                Finished = false, TimeStartUnixSeconds = 1800000000, TimeUsedSeconds = 5,
                Score = 1, PaperScore = 1,
                Items = [new PracticeTakeItem
                {
                    QuestionId = "question-1", Correct = true, SpentSeconds = 5, Score = 1, Percent = 100,
                    DoingMilliseconds = 5000, ViewKeyMilliseconds = 0
                }]
            }, cancellationToken: cancellationToken);
        }
        catch (LotusLmsApiException error) { return error; }
        throw new Exception("FAIL: save-take fixture should fail");
    }

    private sealed class BrokenResponseContent(string token) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("Response disconnected: " + token);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
