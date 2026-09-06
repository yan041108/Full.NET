using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Document.Providers.OfficePreview;

/// <summary>将 Office 源文件转换为 PDF 的可插拔 Provider 边界。</summary>
public interface IDocumentOfficePreviewConversionProvider
{
    /// <summary>Provider 稳定键，与配置 <see cref="Configuration.DocumentOfficePreviewConversionOptions.ProviderKey"/> 对齐。</summary>
    string ProviderKey { get; }

    /// <summary>在隔离工作目录中执行转换并返回 PDF 流。</summary>
    /// <param name="request">源文件元数据与可读流。</param>
    /// <param name="workingDirectory">本轮任务专用临时目录，不得写入目录外路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<DocumentOfficePreviewConversionOutput>> ConvertAsync(
        DocumentOfficePreviewConversionInput request,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>Office 预览转换输入。</summary>
/// <param name="SourceFileName">原始文件名，用于推断扩展名。</param>
/// <param name="SourceMimeType">源 MIME 类型。</param>
/// <param name="SourceContent">源文件只读流，由调用方负责释放。</param>
/// <param name="SourceLength">源文件字节长度。</param>
public sealed record DocumentOfficePreviewConversionInput(
    string SourceFileName,
    string SourceMimeType,
    Stream SourceContent,
    long SourceLength);

/// <summary>Office 预览转换输出。</summary>
/// <param name="PdfContent">PDF 只读流，由调用方负责释放。</param>
/// <param name="PdfLength">PDF 字节长度。</param>
public sealed record DocumentOfficePreviewConversionOutput(
    Stream PdfContent,
    long PdfLength);
