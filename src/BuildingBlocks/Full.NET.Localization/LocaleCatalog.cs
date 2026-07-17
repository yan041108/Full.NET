using System.Collections.ObjectModel;

namespace Full.NET.Localization;

/// <summary>
/// 提供由全栈语言治理契约编译得到的服务端语言目录。
/// </summary>
/// <remarks>
/// 别名只用于输入适配；公共 API、持久化值与跨服务契约必须继续使用规范标签。
/// </remarks>
public static class LocaleCatalog
{
    /// <summary>
    /// 中文（中国大陆）规范语言标签。
    /// </summary>
    public const string Chinese = "zh-CN";

    /// <summary>
    /// 英文（美国）规范语言标签。
    /// </summary>
    public const string English = "en-US";

    /// <summary>
    /// 默认规范语言标签。
    /// </summary>
    public const string DefaultLocale = Chinese;

    /// <summary>
    /// 获取当前生产资源支持的规范语言标签。
    /// </summary>
    public static IReadOnlyList<string> SupportedLocales { get; } =
        Array.AsReadOnly([Chinese, English]);

    /// <summary>
    /// 获取外部别名到规范语言标签的只读映射。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Aliases { get; } =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["zh"] = Chinese,
                ["zh-Hans"] = Chinese,
                ["zh-SG"] = Chinese,
                ["en"] = English,
                ["en-GB"] = English,
            });
}
