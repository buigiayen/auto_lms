# LotusLMS.Http

Reusable `HttpClientFactory` support for optional authenticated web proxies.

```json
{
  "Http": {
    "Proxy": {
      "Enabled": true,
      "Address": "http://proxy.example:8080",
      "Username": "proxy-user",
      "Password": "load-from-a-secret-provider",
      "Domain": "",
      "BypassOnLocal": true,
      "UseDefaultCredentials": false
    }
  }
}
```

```csharp
services.AddWebProxySupport(
    configuration.GetSection(WebProxyOptions.SectionName));

services
    .AddHttpClient("LotusLMS")
    .UseConfiguredWebProxy();
```

When `Enabled` is `false`, the handler connects directly. When it is `true`,
the address and credential combination are validated during host startup.
Addresses support HTTP, HTTPS, and SOCKS5 (for example,
`socks5://160.250.166.18:11548`). Use the provider's SOCKS port for SOCKS5;
leave username/password empty when authentication is not required.
Applications should supply proxy passwords through a secret provider or
environment variable and must not write option values to logs.
