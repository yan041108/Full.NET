using Full.NET.Modules.Ocr.Domain;
using Full.NET.Modules.Ocr.Persistence;

namespace Full.NET.Modules.Ocr.Connectivity;

/// <summary>PaddleOCR 身份证识别的受控远程边界，禁止在数据库事务内调用。</summary>
internal interface IPaddleOcrIdCardClient
{
    /// <summary>将图片发送到 Provider 并解析身份证字段。</summary>
    /// <param name="config">Provider 配置。</param>
    /// <param name="apiKey">已解保护的 API 密钥；未配置时为空。</param>
    /// <param name="content">源图片流；调用方负责释放。</param>
    /// <param name="contentType">内容类型。</param>
    /// <param name="fileName">原始文件名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>识别结果；网络超时必须向外抛出而不是伪装成业务失败。</returns>
    Task<(bool Succeeded, OcrIdCardParsedResult? Result, string RawJson, string Message)> RecognizeAsync(
        OcrProviderConfigRecord config,
        string? apiKey,
        Stream content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>执行轻量连通性探测。</summary>
    /// <param name="config">Provider 配置。</param>
    /// <param name="apiKey">已解保护的 API 密钥；未配置时为空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>探测结果。</returns>
    Task<(bool Succeeded, string Message)> TestAsync(
        OcrProviderConfigRecord config,
        string? apiKey,
        CancellationToken cancellationToken = default);
}
