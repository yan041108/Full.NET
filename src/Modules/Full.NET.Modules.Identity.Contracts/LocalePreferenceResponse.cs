namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 当前账号已保存的规范语言偏好及其独立并发版本。
/// </summary>
public sealed record LocalePreferenceResponse(
    string PreferredLocale,
    int ProfileVersion);
