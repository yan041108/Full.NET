using System.Globalization;
using System.Resources;

namespace Full.NET.Hosting.Api;

/// <summary>为显式注册的生成实体提供标准错误资源，保留实体机器码并使用请求指定的规范文化。</summary>
public sealed class GeneratedCrudErrorResourceSource : IErrorResourceSource
{
    private static readonly ResourceManager Resources = new(
        "Full.NET.Hosting.Resources.GeneratedCrudErrors", typeof(GeneratedCrudErrorResourceSource).Assembly);

    /// <summary>注册精确实体前缀；不会接管其他模块的错误消息。</summary>
    public GeneratedCrudErrorResourceSource(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (!prefix.EndsWith(".", StringComparison.Ordinal)) throw new ArgumentException("错误码前缀必须以点结束。", nameof(prefix));
        Prefix = prefix;
    }

    /// <inheritdoc />
    public string Prefix { get; }

    /// <inheritdoc />
    public bool TryGetTemplate(string code, CultureInfo culture, out string template)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(culture);
        template = code.StartsWith(Prefix, StringComparison.Ordinal)
            ? Resources.GetString(code[Prefix.Length..], culture) ?? string.Empty : string.Empty;
        return template.Length > 0;
    }
}
