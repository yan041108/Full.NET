using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Security;

/// <summary>启动时校验请求签名认证配置边界，避免运行时才发现无效阈值。</summary>
internal sealed class SignatureAuthenticationOptionsValidator
    : IValidateOptions<SignatureAuthenticationOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        SignatureAuthenticationOptions options)
    {
        var failures = new List<string>();
        if (options.MaxBodyBytes < SignatureAuthenticationOptions.MinBodyBytesLimit
            || options.MaxBodyBytes > SignatureAuthenticationOptions.MaxBodyBytesLimit)
        {
            failures.Add(
                $"Identity:Signature:MaxBodyBytes must be between "
                + $"{SignatureAuthenticationOptions.MinBodyBytesLimit} and "
                + $"{SignatureAuthenticationOptions.MaxBodyBytesLimit}.");
        }

        if (options.ClockSkewSeconds < options.MinClockSkewSeconds
            || options.ClockSkewSeconds > options.MaxClockSkewSeconds)
        {
            failures.Add(
                $"Identity:Signature:ClockSkewSeconds must be between "
                + $"{options.MinClockSkewSeconds} and {options.MaxClockSkewSeconds}.");
        }

        if (options.NonceRetentionSeconds < 1)
        {
            failures.Add("Identity:Signature:NonceRetentionSeconds must be at least 1.");
        }

        if (options.MinNonceLength < 1
            || options.MaxNonceLength < options.MinNonceLength)
        {
            failures.Add(
                "Identity:Signature nonce length bounds are invalid.");
        }

        if (options.MaxAccessKeyIdLength < 1)
        {
            failures.Add("Identity:Signature:MaxAccessKeyIdLength must be at least 1.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}