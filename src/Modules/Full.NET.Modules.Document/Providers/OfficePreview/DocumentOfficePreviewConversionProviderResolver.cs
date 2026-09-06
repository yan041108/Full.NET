using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Providers.OfficePreview;

/// <summary>按配置解析当前启用的 Office 预览转换 Provider。</summary>
internal sealed class DocumentOfficePreviewConversionProviderResolver(
    IEnumerable<IDocumentOfficePreviewConversionProvider> providers,
    IOptionsMonitor<DocumentOfficePreviewConversionOptions> options)
{
    private readonly IReadOnlyDictionary<string, IDocumentOfficePreviewConversionProvider> _providers =
        providers.ToDictionary(provider => provider.ProviderKey, StringComparer.Ordinal);

    /// <summary>返回配置指定的 Provider；未知键时回退到 disabled。</summary>
    public IDocumentOfficePreviewConversionProvider ResolveCurrent()
    {
        var providerKey = options.CurrentValue.ProviderKey;
        if (_providers.TryGetValue(providerKey, out var provider))
        {
            return provider;
        }

        return _providers[DocumentOfficePreviewConversionProviderKeys.Disabled];
    }
}
