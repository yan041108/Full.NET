using System.Text.RegularExpressions;

namespace Full.NET.Modules.Regions.Domain;

/// <summary>
/// 行政区域稳定编码校验规则；编码为 1–12 位数字，与国标统计用区划代码风格对齐。
/// </summary>
internal static class AdministrativeRegionCodeRules
{
    private static readonly Regex CodePattern = new(
        "^\\d{1,12}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 尝试规范化并校验区域编码。
    /// </summary>
    /// <param name="value">原始编码。</param>
    /// <param name="code">校验通过时输出的规范化编码。</param>
    /// <returns>编码合法时返回 <see langword="true"/>。</returns>
    public static bool TryNormalize(string? value, out string code)
    {
        code = value?.Trim() ?? string.Empty;
        return code.Length > 0 && CodePattern.IsMatch(code);
    }

    /// <summary>
    /// 校验层级是否在允许范围内。
    /// </summary>
    /// <param name="level">层级值。</param>
    /// <returns>层级合法时返回 <see langword="true"/>。</returns>
    public static bool IsValidLevel(int level) => level is >= 1 and <= 5;
}
