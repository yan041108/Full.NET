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
        var current = options.CurrentValue.ExternalHttp;
        if (string.IsNullOrWhiteSpace(current.BaseUrl))
        {
            return Result<DocumentOfficePreviewConversionOutput>.Failure(
                new Error(
                    DocumentErrorCodes.OfficePreviewProviderDisabled,
                    "External HTTP office preview conversion endpoint is not configured.",
                    ErrorType.BusinessRule));
        }

        using var client = httpClientFactory.CreateClient(nameof(ExternalHttpDocumentOfficePreviewConversionProvider));
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.TimeoutSeconds, 5, 3600));
        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(request.SourceContent);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(request.SourceMimeType);
        form.Add(streamContent, "file", request.SourceFileName);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, current.BaseUrl)
        {
            Content = form,
        };
        if (!string.IsNullOrWhiteSpace(current.ApiKey))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", current.ApiKey);
        }

        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return Result<DocumentOfficePreviewConversionOutput>.Failure(
                new Error(
                    DocumentErrorCodes.OfficePreviewConversionFailed,
                    "External HTTP office preview conversion failed.",
                    ErrorType.BusinessRule));
        }

        var pdfStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (pdfStream.CanSeek)
        {
            return Result<DocumentOfficePreviewConversionOutput>.Success(
                new DocumentOfficePreviewConversionOutput(pdfStream, pdfStream.Length));
        }

        var memory = new MemoryStream();
        await pdfStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        await pdfStream.DisposeAsync().ConfigureAwait(false);
        memory.Position = 0;
        return Result<DocumentOfficePreviewConversionOutput>.Success(
            new DocumentOfficePreviewConversionOutput(memory, memory.Length));
    }
}
