using System.Globalization;

namespace Full.NET.Localization;

/// <summary>
/// 从当前异步执行上下文读取 UI Culture，并转换为规范语言标签。
/// </summary>
/// <param name="normalizer">用于把当前 UI Culture 转换为规范标签的语言规范化器。</param>
public sealed class LocaleContext(ILocaleNormalizer normalizer) : ILocaleContext
{
    /// <inheritdoc />
    public string CurrentLocale => normalizer.Normalize(CultureInfo.CurrentUICulture.Name);
}
