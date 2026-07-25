using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.RateLimiting;

internal sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        if (options.GlobalApiPermitLimitPerMinute < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{RateLimitingOptions.SectionName}:GlobalApiPermitLimitPerMinute must be zero or greater.");
        }

        return ValidateOptionsResult.Success;
    }
}
