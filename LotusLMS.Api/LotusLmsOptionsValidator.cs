using Microsoft.Extensions.Options;

namespace LotusLMS.Api;

internal sealed class LotusLmsOptionsValidator : IValidateOptions<LotusLmsOptions>
{
    public ValidateOptionsResult Validate(string? name, LotusLmsOptions options)
    {
        var failures = new List<string>();

        ValidateAbsoluteHttpUri(options.BaseUrl, "BaseUrl", failures);
        ValidateAbsoluteHttpUri(options.WebOrigin, "WebOrigin", failures);

        if (string.IsNullOrWhiteSpace(options.Tenant))
        {
            failures.Add($"{LotusLmsOptions.SectionName}:Tenant is required.");
        }

        if (string.IsNullOrWhiteSpace(options.DeviceIdentityDirectory))
        {
            failures.Add($"{LotusLmsOptions.SectionName}:DeviceIdentityDirectory is required.");
        }

        if (options.RequestTimeoutSeconds is < 1 or > 600)
        {
            failures.Add(
                $"{LotusLmsOptions.SectionName}:RequestTimeoutSeconds must be between 1 and 600.");
        }

        if (options.LogApiResponses && string.IsNullOrWhiteSpace(options.ApiResponseLogDirectory))
        {
            failures.Add(
                $"{LotusLmsOptions.SectionName}:ApiResponseLogDirectory is required when API response logging is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.UserAgent))
        {
            failures.Add($"{LotusLmsOptions.SectionName}:UserAgent is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateAbsoluteHttpUri(
        string? value,
        string propertyName,
        ICollection<string> failures)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add(
                $"{LotusLmsOptions.SectionName}:{propertyName} must be an absolute HTTP or HTTPS URI.");
        }
    }
}
