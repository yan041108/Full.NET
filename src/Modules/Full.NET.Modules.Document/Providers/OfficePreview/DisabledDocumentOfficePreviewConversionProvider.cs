using Full.NET.Abstractions.Results;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.Modules.Document.Providers.OfficePreview;

/// <summary>显式禁用 Office 预览转换，用于未配置外部引擎时的失败关闭。</summary>
internal sealed class DisabledDocumentOfficePreviewConversionProvider : IDocumentOfficePreviewConversionProvider
{
    public string ProviderKey => DocumentOfficePreviewConversionProviderKeys.Disabled;

    public Task<Result<DocumentOfficePreviewConversionOutput>> ConvertAsync(
        DocumentOfficePreviewConversionInput request,
        string workingDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<DocumentOfficePreviewConversionOutput>.Failure(
            new Error(
                DocumentErrorCodes.OfficePreviewProviderDisabled,
                "Office preview conversion provider is disabled.",
                ErrorType.BusinessRule)));
}
