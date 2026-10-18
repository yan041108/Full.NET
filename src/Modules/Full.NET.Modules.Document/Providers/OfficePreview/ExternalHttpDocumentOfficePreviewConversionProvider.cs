using System.Net.Http.Headers;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Providers.OfficePreview;

/// <summary>通过受控 HTTP 网关调用运维部署的外部 Office→PDF 转换服务。</summary>
internal sealed class ExternalHttpDocumentOfficePreviewConversionProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<DocumentOfficePreviewConversionOptions> options) : IDocumentOfficePreviewConversionProvider
{
    public string ProviderKey => DocumentOfficePreviewConversionProviderKeys.ExternalHttp;

    public async Task<Result<DocumentOfficePreviewConversionOutput>> ConvertAsync(
        DocumentOfficePreviewConversionInput request,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var snapshot = options.CurrentValue;
        var current = snapshot.ExternalHttp;
        if (string.IsNullOrWhiteSpace(current.BaseUrl))
        {
            return Result<DocumentOfficePreviewConversionOutput>.Failure(
                new Error(
                    DocumentErrorCodes.OfficePreviewProviderDisabled,
                    "External HTTP office preview conversion endpoint is not configured.",
                    ErrorType.BusinessRule));
        }

        // ResponseHeadersRead 后 HttpClient.Timeout 不覆盖正文；同一令牌约束上传、响应头和完整 PDF 读取。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(snapshot.TimeoutSeconds, 5, 3600)));
        try
        {
            DocumentPreviewContentBudget.CheckDeclaredLength(request.SourceLength, snapshot.MaxInputBytes, DocumentErrorCodes.OfficePreviewInputTooLarge);
            using var client = httpClientFactory.CreateClient(nameof(ExternalHttpDocumentOfficePreviewConversionProvider));
            client.Timeout = Timeout.InfiniteTimeSpan;
            using var form = new MultipartFormDataContent();
            using var input = new DocumentPreviewBudgetStream(request.SourceContent, snapshot.MaxInputBytes, DocumentErrorCodes.OfficePreviewInputTooLarge);
            var streamContent = new StreamContent(input);
            streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(request.SourceMimeType);
            if (request.SourceContent.CanSeek) streamContent.Headers.ContentLength = request.SourceContent.Length - request.SourceContent.Position;
            form.Add(streamContent, "file", request.SourceFileName);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, current.BaseUrl) { Content = form };
            if (!string.IsNullOrWhiteSpace(current.ApiKey)) httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", current.ApiKey);
            using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Failure(DocumentErrorCodes.OfficePreviewConversionFailed);
            var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            // 返回独立拥有的有界流；响应在退出方法时释放，不能把其内部流直接交给 Worker。
            var output = await DocumentPreviewContentBudget.ReadOutputAsync(body, response.Content.Headers.ContentLength, snapshot.MaxOutputBytes, timeout.Token).ConfigureAwait(false);
            return Result<DocumentOfficePreviewConversionOutput>.Success(new(output, output.Length));
        }
        catch (DocumentPreviewContentLimitException exception) { return Failure(exception.Code); }
        catch (HttpRequestException exception) when (exception.InnerException is DocumentPreviewContentLimitException)
        {
            return Failure(((DocumentPreviewContentLimitException)exception.InnerException).Code);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Failure(DocumentErrorCodes.OfficePreviewConversionFailed); }
        catch (InvalidDataException) { return Failure(DocumentErrorCodes.OfficePreviewConversionFailed); }
    }

    private static Result<DocumentOfficePreviewConversionOutput> Failure(string code) =>
        Result<DocumentOfficePreviewConversionOutput>.Failure(new(code, "Office preview conversion failed or exceeded its configured budget.", ErrorType.BusinessRule));
}
