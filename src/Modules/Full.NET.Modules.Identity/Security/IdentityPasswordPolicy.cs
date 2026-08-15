using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// 强密码策略校验器。要求 12+ 长度、大写/小写/数字/非字母数字四类字符，
/// 并为每类违规返回独立的可翻译错误码；当前不做历史密码重复或弱字典比较，
/// 留作后续策略扩展点。
/// </summary>
internal static class IdentityPasswordPolicy
{
    /// <summary>密码最小长度（字符数）。</summary>
    public const int MinimumLength = 12;

    /// <summary>
    /// 执行密码策略校验并返回全部违规项集合；空集合表示通过。
    /// 不含密码明文或可推测片段，返回结果可安全写入响应。
    /// </summary>
    public static IReadOnlyList<IdentityPasswordPolicyViolation> Validate(
        string? password)
    {
        var violations = new List<IdentityPasswordPolicyViolation>();
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            violations.Add(new IdentityPasswordPolicyViolation(
                IdentityErrorCodes.PasswordMinimumLength,
                $"Password must contain at least {MinimumLength} characters.",
                new Dictionary<string, object?>
                {
                    ["MinLength"] = MinimumLength,
                }));
        }

        if (string.IsNullOrEmpty(password) || !password.Any(char.IsUpper))
        {
            violations.Add(new IdentityPasswordPolicyViolation(
                IdentityErrorCodes.PasswordUppercaseRequired,
                "Password must contain an uppercase letter.",
                new Dictionary<string, object?>()));
        }

        if (string.IsNullOrEmpty(password) || !password.Any(char.IsLower))
        {
            violations.Add(new IdentityPasswordPolicyViolation(
                IdentityErrorCodes.PasswordLowercaseRequired,
                "Password must contain a lowercase letter.",
                new Dictionary<string, object?>()));
        }

        if (string.IsNullOrEmpty(password) || !password.Any(char.IsDigit))
        {
            violations.Add(new IdentityPasswordPolicyViolation(
                IdentityErrorCodes.PasswordDigitRequired,
                "Password must contain a number.",
                new Dictionary<string, object?>()));
        }

        if (string.IsNullOrEmpty(password) || password.All(char.IsLetterOrDigit))
        {
            violations.Add(new IdentityPasswordPolicyViolation(
                IdentityErrorCodes.PasswordNonAlphanumericRequired,
                "Password must contain a non-alphanumeric character.",
                new Dictionary<string, object?>()));
        }

        return violations;
    }
}

/// <summary>
/// 表示不含密码原值、可安全映射到传输契约的密码策略违反项。
/// </summary>
/// <param name="Code">稳定 Identity 错误码。</param>
/// <param name="DefaultMessage">资源缺失时的安全英文回退。</param>
/// <param name="Arguments">仅包含允许公开的格式化参数。</param>
internal sealed record IdentityPasswordPolicyViolation(
    string Code,
    string DefaultMessage,
    IReadOnlyDictionary<string, object?> Arguments);
