namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 当前账号已保存的规范语言偏好及其独立并发版本。
/// </summary>
/// <param name="PreferredLocale">按 BCP 47 保存的首选语言标签，供前后端恢复统一显示语言。</param>
/// <param name="ProfileVersion">语言偏好所属档案版本；写入方必须回传最新值以避免并发覆盖。</param>
public sealed record LocalePreferenceResponse(
    string PreferredLocale,
    int ProfileVersion);
