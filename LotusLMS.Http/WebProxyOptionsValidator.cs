using Microsoft.Extensions.Options;

namespace LotusLMS.Http;

internal sealed class WebProxyOptionsValidator : IValidateOptions<WebProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, WebProxyOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Address))
        {
            failures.Add($"{WebProxyOptions.SectionName}:Address is required when the proxy is enabled.");
        }
        else if (!Uri.TryCreate(options.Address, UriKind.Absolute, out var address)
                 || address.Scheme is not ("http" or "https" or "socks5")
                 || string.IsNullOrWhiteSpace(address.Host)
                 || address.Port is < 1 or > 65535)
        {
            failures.Add($"{WebProxyOptions.SectionName}:Address must be an absolute HTTP, HTTPS or SOCKS5 URI with a valid port.");
        }

        var hasUsername = !string.IsNullOrWhiteSpace(options.Username);
        var hasExplicitCredentialParts = hasUsername
                                         || !string.IsNullOrEmpty(options.Password)
                                         || !string.IsNullOrWhiteSpace(options.Domain);

        if (options.UseDefaultCredentials && hasExplicitCredentialParts)
        {
            failures.Add(
                $"{WebProxyOptions.SectionName} cannot combine UseDefaultCredentials with explicit credentials.");
        }
        else if (!options.UseDefaultCredentials && !hasUsername
                 && (!string.IsNullOrEmpty(options.Password) || !string.IsNullOrWhiteSpace(options.Domain)))
        {
            failures.Add(
                $"{WebProxyOptions.SectionName}:Username is required when Password or Domain is configured.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
