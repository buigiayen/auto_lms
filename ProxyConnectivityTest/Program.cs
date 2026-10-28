using System.Net;
using System.Text.RegularExpressions;

const string defaultTestUrl = "https://api.ipify.org?format=json";
string? enteredProxyAddress = null;
string? proxyUsername = null;
string? proxyPassword = null;

try
{
    enteredProxyAddress = Environment.GetEnvironmentVariable("PROXY_ADDRESS");
    if (string.IsNullOrWhiteSpace(enteredProxyAddress))
    {
        Console.Write("Proxy (host:port:username:password, socks5://host:port, or http://host:port): ");
        enteredProxyAddress = Console.ReadLine();
    }

    if (string.IsNullOrWhiteSpace(enteredProxyAddress))
        throw new ArgumentException("A proxy URL is required.");

    proxyUsername = Environment.GetEnvironmentVariable("PROXY_USERNAME");
    proxyPassword = Environment.GetEnvironmentVariable("PROXY_PASSWORD");
    var proxyAddress = enteredProxyAddress.Trim();
    var providerFormat = false;
    if (!proxyAddress.Contains("://", StringComparison.Ordinal))
    {
        var match = Regex.Match(proxyAddress,
            @"^(?<host>\[[^\]]+\]|[^:]+):(?<port>\d+):(?<username>[^:]+):(?<password>.*)$");
        if (!match.Success
            || !int.TryParse(match.Groups["port"].Value, out var providerPort)
            || providerPort is < 1 or > 65535)
        {
            throw new ArgumentException(
                "Use host:port:username:password (HTTP proxy), socks5://host:port, http://host:port, or https://host:port.");
        }

        var providerHost = match.Groups["host"].Value;
        proxyAddress = $"http://{providerHost}:{providerPort}";
        proxyUsername ??= match.Groups["username"].Value;
        proxyPassword ??= match.Groups["password"].Value;
        providerFormat = true;
    }

    var proxyUri = new Uri(proxyAddress, UriKind.Absolute);
    if (proxyUri.Scheme is not ("socks5" or "http" or "https")
        || string.IsNullOrWhiteSpace(proxyUri.Host)
        || proxyUri.Port is < 1 or > 65535
        || proxyUri.AbsolutePath is not ("" or "/")
        || proxyUri.Query.Length > 0
        || proxyUri.Fragment.Length > 0)
    {
        throw new ArgumentException(
            "Use socks5://host:port, http://host:port, or https://host:port.");
    }

    if (proxyUri.UserInfo.Length > 0)
    {
        var userInfo = proxyUri.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
        var separator = userInfo.IndexOf(':');
        if (separator <= 0)
            throw new ArgumentException("Proxy URL credentials must use username:password.");

        proxyUsername ??= userInfo[..separator];
        proxyPassword ??= userInfo[(separator + 1)..];
    }
    else if (!providerFormat && string.IsNullOrWhiteSpace(proxyUsername) && !Console.IsInputRedirected)
    {
        Console.Write("Proxy username (leave empty for no authentication): ");
        proxyUsername = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(proxyUsername) && proxyPassword is null)
        {
            Console.Write("Proxy password: ");
            proxyPassword = ReadPassword();
            Console.WriteLine();
        }
    }

    var proxyEndpoint = new UriBuilder(proxyUri)
    {
        UserName = "",
        Password = "",
        Path = "",
        Query = "",
        Fragment = ""
    }.Uri;
    var webProxy = new WebProxy(proxyEndpoint)
    {
        Credentials = string.IsNullOrEmpty(proxyUsername)
            ? null
            : new NetworkCredential(proxyUsername, proxyPassword ?? "")
    };

    var testUrl = Environment.GetEnvironmentVariable("PROXY_TEST_URL");
    if (string.IsNullOrWhiteSpace(testUrl))
        testUrl = defaultTestUrl;
    if (!Uri.TryCreate(testUrl, UriKind.Absolute, out var destination)
        || destination.Scheme is not ("http" or "https"))
    {
        throw new ArgumentException("PROXY_TEST_URL must be an absolute HTTP or HTTPS URL.");
    }

    using var handler = new SocketsHttpHandler
    {
        Proxy = webProxy,
        UseProxy = true,
        ConnectTimeout = TimeSpan.FromSeconds(15)
    };
    using var client = new HttpClient(handler)
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    using var request = new HttpRequestMessage(HttpMethod.Get, destination);
    request.Headers.ConnectionClose = true;

    using var response = await client.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(
            $"Proxy request failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        return 1;
    }

    Console.WriteLine($"Proxy connection succeeded. Response: {body}");
    return 0;
}
catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
{
    var message = exception.Message;
    foreach (var secret in new[] { enteredProxyAddress, proxyUsername, proxyPassword })
    {
        if (!string.IsNullOrEmpty(secret))
            message = message.Replace(secret, "[redacted]", StringComparison.Ordinal);
    }
    Console.Error.WriteLine($"Proxy connection failed: {exception.GetType().Name}: {message}");
    return 1;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"Invalid proxy configuration: {exception.Message}");
    return 2;
}
catch (UriFormatException)
{
    Console.Error.WriteLine("Invalid proxy URL. Check its scheme, host, and port.");
    return 2;
}

static string ReadPassword()
{
    var password = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
                password.Length--;
            continue;
        }
        if (!char.IsControl(key.KeyChar))
            password.Append(key.KeyChar);
    }

    return password.ToString();
}
