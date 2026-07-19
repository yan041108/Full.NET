using System.Collections.Frozen;

namespace Full.NET.Hosting.Api;

/// <summary>
/// Pre-v1 协议名称兼容边界：集中维护 <c>PreV1NameMapV1</c> 中需要客户端/资源回退的 error_code 映射。
/// </summary>
public static class PreV1ProtocolCompatibility
{
    private static readonly FrozenDictionary<string, string> LegacyToCanonical =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["identity.bootstrap.invalid-password"] = "identity.bootstrap.invalid_password",
            ["identity.bootstrap.invalid-profile"] = "identity.bootstrap.invalid_profile",
            ["identity.login-succeeded"] = "identity.login_succeeded",
            ["tenancy.domain-exists"] = "tenancy.domain_exists",
            ["tenancy.host-not-found"] = "tenancy.host_not_found",
            ["tenancy.identifier-exists"] = "tenancy.identifier_exists",
            ["tenancy.not-found"] = "tenancy.not_found",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> CanonicalToLegacy =
        LegacyToCanonical.ToFrozenDictionary(
            pair => pair.Value,
            pair => pair.Key,
            StringComparer.Ordinal);

    /// <summary>
    /// 将已知 legacy error_code 规范化为 canonical；未知值原样返回。
    /// </summary>
    public static string NormalizeErrorCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return LegacyToCanonical.GetValueOrDefault(code, code);
    }

    /// <summary>
    /// 判断给定值是否为已登记的 legacy error_code。
    /// </summary>
    public static bool IsLegacyErrorCode(string code) =>
        !string.IsNullOrWhiteSpace(code) && LegacyToCanonical.ContainsKey(code);

    /// <summary>
    /// 在 Pre-v1 Legacy Profile 下将 canonical error_code 映射回 legacy 对外码。
    /// </summary>
    public static string ToLegacyErrorCode(string canonicalCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalCode);
        return CanonicalToLegacy.GetValueOrDefault(canonicalCode, canonicalCode);
    }

    /// <summary>
    /// 尝试解析 canonical error_code 对应的 legacy 资源/兼容键。
    /// </summary>
    public static bool TryGetLegacyErrorCodeAlias(
        string canonicalCode,
        out string legacyCode) =>
        CanonicalToLegacy.TryGetValue(canonicalCode, out legacyCode!);

    /// <summary>
    /// 已登记 legacy error_code 的 canonical 目标集合。
    /// </summary>
    public static IReadOnlyCollection<string> CanonicalErrorCodes =>
        CanonicalToLegacy.Keys;
}
