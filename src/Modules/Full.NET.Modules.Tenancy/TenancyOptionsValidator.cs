using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenancyOptionsValidator : IValidateOptions<TenancyOptions>
{
    public ValidateOptionsResult Validate(string? name, TenancyOptions options)
    {
        if (options.HostDomains is null)
        {
            return ValidateOptionsResult.Fail(
                "Tenancy:HostDomains must not be null.");
        }

        var failures = new List<string>();
        var uniqueDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in options.HostDomains)
        {
            if (string.IsNullOrWhiteSpace(domain)
                || !string.Equals(domain, domain.Trim(), StringComparison.Ordinal)
                || Uri.CheckHostName(domain) == UriHostNameType.Unknown)
            {
                failures.Add(
                    "Tenancy:HostDomains entries must be host names without "
                    + "scheme, port, path, wildcard, or surrounding whitespace.");
                continue;
            }

            if (!uniqueDomains.Add(domain))
            {
                failures.Add(
                    "Tenancy:HostDomains entries must be unique "
                    + "ignoring case.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
