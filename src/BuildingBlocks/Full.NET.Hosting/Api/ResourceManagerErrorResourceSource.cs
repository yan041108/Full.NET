using System.Globalization;
using System.Resources;

namespace Full.NET.Hosting.Api;

/// <summary>
/// 使用 <see cref="ResourceManager"/> 缓存读取模块编译资源的错误资源来源。
/// </summary>
public class ResourceManagerErrorResourceSource : IErrorResourceSource
{
    private readonly ResourceManager _resourceManager;

    /// <summary>
    /// 初始化一个错误资源来源。
    /// </summary>
    /// <param name="prefix">该来源负责的稳定错误码前缀。</param>
    /// <param name="resourceManager">指向已编译 .resx 的资源管理器。</param>
    public ResourceManagerErrorResourceSource(
        string prefix,
        ResourceManager resourceManager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(resourceManager);
        Prefix = prefix;
        _resourceManager = resourceManager;
    }

    /// <inheritdoc />
    public string Prefix { get; }

    /// <inheritdoc />
    public bool TryGetTemplate(
        string code,
        CultureInfo culture,
        out string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(culture);

        template = _resourceManager.GetString(code, culture) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(template);
    }
}
