namespace Full.NET.Localization;

/// <summary>
/// 定义语言治理与规范化能力返回的稳定错误码。
/// </summary>
public static class LocalizationErrorCodes
{
    /// <summary>本地化错误码前缀。</summary>
    public const string Prefix = "localization.";

    /// <summary>调用方提交的非空语言标签不在支持目录或别名中。</summary>
    public const string UnsupportedLocale = "localization.unsupported_locale";

    /// <summary>获取当前目录中的全部稳定错误码。</summary>
    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly([UnsupportedLocale]);
}
