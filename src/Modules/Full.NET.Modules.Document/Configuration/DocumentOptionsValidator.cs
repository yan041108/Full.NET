using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Configuration;

internal sealed class DocumentOptionsValidator : IValidateOptions<DocumentOptions>
{
    public ValidateOptionsResult Validate(string? name, DocumentOptions options)
    {
        if (options.AnonymousShareAccessRateLimitPermitLimitPerMinute < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{DocumentOptions.SectionName}:AnonymousShareAccessRateLimitPermitLimitPerMinute must be at least 1.");
        }

        return ValidateOptionsResult.Success;
    }
}
