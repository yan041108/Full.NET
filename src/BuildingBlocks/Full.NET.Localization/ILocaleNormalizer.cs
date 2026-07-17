namespace Full.NET.Localization;

/// <summary>
/// 将受支持的语言别名转换为 Full.NET 公共契约使用的规范标签。
/// </summary>
public interface ILocaleNormalizer
{
    /// <summary>
    /// 获取无法识别请求语言时使用的规范语言标签。
    /// </summary>
    string DefaultLocale { get; }

    /// <summary>
    /// 获取当前服务端资源支持的规范语言标签。
    /// </summary>
    IReadOnlyList<string> SupportedLocales { get; }

    /// <summary>
    /// 将规范标签或已登记别名转换为规范标签，未知值回退到默认语言。
    /// </summary>
    /// <param name="requestedLocale">调用方提供的语言标签或别名。</param>
    /// <returns>受支持的规范语言标签。</returns>
    string Normalize(string? requestedLocale);

    /// <summary>
    /// 判断输入是否为受支持的规范标签或已登记别名。
    /// </summary>
    /// <param name="locale">待判断的语言标签。</param>
    /// <returns>可无损映射到规范标签时返回 <see langword="true"/>。</returns>
    bool IsSupported(string? locale);
}
