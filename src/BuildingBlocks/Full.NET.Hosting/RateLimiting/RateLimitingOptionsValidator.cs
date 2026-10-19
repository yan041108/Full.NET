using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.RateLimiting;

/// <summary>启动期校验全局限流配置非负；具体分区策略由配置器解释。</summary>
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
