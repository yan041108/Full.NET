using Microsoft.Extensions.Options;

/// <summary>启动期校验模块配置；非法组合在宿主启动时失败。</summary>
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
