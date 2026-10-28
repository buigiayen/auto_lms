using System.Collections.Concurrent;
using System.Net;
using LotusLMS.Api;
using Microsoft.Extensions.Options;

internal static class DeviceIdentityChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
            checks++;
        }

        var directory = Path.Combine(Path.GetTempPath(), "lotuslms-device-" + Guid.NewGuid().ToString("N"));
        using var handler = new LoginHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://lms.invalid/") };
        var options = new LotusLmsOptions
        {
            GenerateRequestTokens = false,
            LogApiResponses = false,
            DeviceIdentityDirectory = directory
        };
        var login = new LotusLmsLoginRequest { Username = "fixture", Password = "fixture" };

        try
        {
            var first = await new LotusLmsApiClient(http, Options.Create(options)).LoginAsync(login);
            Check(Guid.TryParse(first.DeviceUuid, out _), "missing device ID generates a valid UUID");
            var path = Path.Combine(directory, "device-uuid.txt");
            Check(File.ReadAllText(path).Trim() == first.DeviceUuid, "generated UUID is saved");
            Check(handler.DeviceUuids.TryPeek(out var sent) && sent == first.DeviceUuid,
                "login sends the generated device UUID");

            var second = await new LotusLmsApiClient(http, Options.Create(options)).LoginAsync(login);
            Check(second.DeviceUuid == first.DeviceUuid, "new API client reuses the saved device UUID");
            Check(second.SessionId != first.SessionId, "new login still gets a fresh session ID");

            var blank = await new LotusLmsApiClient(http, Options.Create(options))
                .LoginAsync(login with { DeviceUuid = "  " });
            Check(blank.DeviceUuid == first.DeviceUuid, "blank override uses the persisted device UUID");

            var explicitUuid = Guid.NewGuid().ToString("D");
            var manual = await new LotusLmsApiClient(http, Options.Create(options))
                .LoginAsync(login with { DeviceUuid = " " + explicitUuid + " " });
            Check(manual.DeviceUuid == explicitUuid, "explicit UUID override is preserved and trimmed");
            Check(File.ReadAllText(path).Trim() == first.DeviceUuid, "explicit override does not replace the saved UUID");

            var providerLogin = await new LotusLmsApiClient(http, Options.Create(options)).LoginAsync(login with
            {
                Provider = new LotusLmsProvider
                {
                    BaseUrl = "https://provider-api.invalid",
                    WebOrigin = "https://provider.invalid",
                    Tenant = "provider"
                }
            });
            Check(handler.RequestUris.Last() == "https://provider-api.invalid/user/login",
                "provider API base overrides worker configuration for login");
            Check(handler.Origins.Last() == "https://provider.invalid"
                && handler.Domains.Last() == "provider",
                "login origin and tenant come from the provider assignment");
            Check(providerLogin.Provider?.Tenant == "provider",
                "provider configuration is retained in the authenticated session");
            await new LotusLmsApiClient(http, Options.Create(options)).GetStudentEnrolmentPlansAsync(providerLogin);
            Check(handler.RequestUris.Last().StartsWith("https://provider-api.invalid/", StringComparison.Ordinal)
                && handler.Origins.Last() == "https://provider.invalid"
                && handler.Domains.Last() == "provider",
                "authenticated requests retain the session provider instead of worker defaults");

            var concurrentDirectory = Path.Combine(directory, "concurrent");
            var concurrentOptions = new LotusLmsOptions
            {
                GenerateRequestTokens = false,
                LogApiResponses = false,
                DeviceIdentityDirectory = concurrentDirectory
            };
            var sessions = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
                new LotusLmsApiClient(http, Options.Create(concurrentOptions)).LoginAsync(login))));
            Check(sessions.Select(session => session.DeviceUuid).Distinct().Count() == 1,
                "concurrent first logins agree on one device UUID");
            Check(Guid.TryParse(sessions[0].DeviceUuid, out _), "concurrent creation publishes a complete UUID");
            Check(!Directory.EnumerateFiles(concurrentDirectory, "*.tmp").Any(), "creation leaves no temporary files");

            File.WriteAllText(path, "invalid-uuid");
            try
            {
                await new LotusLmsApiClient(http, Options.Create(options)).LoginAsync(login);
                throw new Exception("Accepted invalid persisted device UUID");
            }
            catch (InvalidDataException)
            {
                checks++;
            }

            return checks;
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class LoginHandler : HttpMessageHandler
    {
        public ConcurrentQueue<string> DeviceUuids { get; } = new();
        public ConcurrentQueue<string> RequestUris { get; } = new();
        public ConcurrentQueue<string?> Origins { get; } = new();
        public ConcurrentQueue<string?> Domains { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var deviceField = form.Split('&').FirstOrDefault(field => field.StartsWith("_sand_device_uuid=", StringComparison.Ordinal));
            if (deviceField is not null)
            {
                DeviceUuids.Enqueue(Uri.UnescapeDataString(deviceField.Split('=', 2)[1]));
            }
            RequestUris.Enqueue(request.RequestUri!.AbsoluteUri);
            Origins.Enqueue(request.Headers.GetValues("Origin").Single());
            var domain = (form + "&" + request.RequestUri.Query.TrimStart('?'))
                .Split('&')
                .LastOrDefault(field => field.StartsWith("_sand_domain=", StringComparison.Ordinal));
            Domains.Enqueue(domain is null ? null : Uri.UnescapeDataString(domain.Split('=', 2)[1]));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"result":{"pass":1,"token":"fixture-token","iid":1,"id":"fixture-id"}}""")
            };
        }
    }
}
