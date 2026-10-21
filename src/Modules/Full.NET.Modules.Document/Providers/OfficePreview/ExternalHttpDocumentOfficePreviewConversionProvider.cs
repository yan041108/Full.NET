using Full.NET.Hosting.Http;
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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.TimeoutSeconds, 5, 3600)));
        cancellationToken = deadline.Token;
        var inputPath = Path.Combine(workingDirectory, $"http-input-{Guid.NewGuid():N}.bin");
        await using var input = new FileStream(inputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await OfficePreviewInputBudget.CopyAsync(request.SourceContent, input,
            Math.Min(options.CurrentValue.MaxInputBytes, options.CurrentValue.MaxTemporaryBytes), cancellationToken).ConfigureAwait(false);
        input.Position = 0;
        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(input);
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

        // 转存到本次任务目录，不能返回随 response 释放的网络流，也不能把整个 PDF 堆积到内存。
        var path = Path.Combine(workingDirectory, $"http-preview-{Guid.NewGuid():N}.pdf");
        var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            var length = await BoundedHttpContent.CopyToAsync(response.Content, output,
                Math.Min(options.CurrentValue.MaxOutputBytes, options.CurrentValue.MaxTemporaryBytes - input.Length), cancellationToken).ConfigureAwait(false);
            output.Position = 0;
            return Result<DocumentOfficePreviewConversionOutput>.Success(new(output, length));
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
