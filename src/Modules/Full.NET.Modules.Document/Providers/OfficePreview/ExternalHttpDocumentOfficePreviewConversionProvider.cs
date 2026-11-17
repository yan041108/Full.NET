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
        cancellationToken.ThrowIfCancellationRequested();
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

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(snapshot.TimeoutSeconds, 5, 3600)));
        try
        {
            var inputBudget = Math.Min(snapshot.MaxInputBytes, snapshot.MaxTemporaryBytes);
            DocumentPreviewContentBudget.CheckDeclaredLength(request.SourceLength, inputBudget, DocumentErrorCodes.OfficePreviewInputTooLarge);
            await using var input = CreateTemporaryFile(workingDirectory, "input.bin");
            using (var source = new DocumentPreviewBudgetStream(request.SourceContent, inputBudget, DocumentErrorCodes.OfficePreviewInputTooLarge))
                await source.CopyToAsync(input, deadline.Token).ConfigureAwait(false);
            input.Position = 0;

            var outputBudget = Math.Min(snapshot.MaxOutputBytes, snapshot.MaxTemporaryBytes - input.Length);
            if (outputBudget <= 0)
                return Failure(DocumentErrorCodes.OfficePreviewOutputTooLarge, "No temporary byte budget remains for the converted PDF.");

            using var client = httpClientFactory.CreateClient(nameof(ExternalHttpDocumentOfficePreviewConversionProvider));
            // 同一截止时间覆盖输入转存、发送、响应头和完整响应正文。
            client.Timeout = Timeout.InfiniteTimeSpan;
            using var form = new MultipartFormDataContent();
            var streamContent = new StreamContent(input);
            streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(request.SourceMimeType);
            form.Add(streamContent, "file", request.SourceFileName);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, current.BaseUrl) { Content = form };
            if (!string.IsNullOrWhiteSpace(current.ApiKey))
                httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", current.ApiKey);

            using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
                return Failure(DocumentErrorCodes.OfficePreviewConversionFailed, "External HTTP office preview conversion failed.");

            return await ReadOutputAsync(response.Content, workingDirectory, outputBudget, deadline.Token).ConfigureAwait(false);
        }
        catch (DocumentPreviewContentLimitException limit)
        {
            return Failure(limit.Code, limit.Message);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(DocumentErrorCodes.OfficePreviewConversionFailed, "External HTTP office preview conversion timed out.");
        }
    }

    private static async Task<Result<DocumentOfficePreviewConversionOutput>> ReadOutputAsync(
        HttpContent content, string workingDirectory, long budget, CancellationToken token)
    {
        DocumentPreviewContentBudget.CheckDeclaredLength(content.Headers.ContentLength ?? 0, budget, DocumentErrorCodes.OfficePreviewOutputTooLarge);
        await using var source = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var limited = new DocumentPreviewBudgetStream(source, budget, DocumentErrorCodes.OfficePreviewOutputTooLarge);
        // 转存到任务目录，让成功结果独立于响应生命周期并限制内存占用。
        var output = CreateTemporaryFile(workingDirectory, "preview.pdf");
        var transferred = false;
        try
        {
            await limited.CopyToAsync(output, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var length = output.Length;
            if (length == 0)
                return Failure(DocumentErrorCodes.OfficePreviewConversionFailed, "External HTTP office preview conversion returned no content.");
            output.Position = 0;
            transferred = true;
            return Result<DocumentOfficePreviewConversionOutput>.Success(new(output, length));
        }
        finally
        {
            if (!transferred) await output.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static FileStream CreateTemporaryFile(string directory, string name) => new(
        Path.Combine(directory, $"http-{Guid.NewGuid():N}-{name}"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
        16 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);

    private static Result<DocumentOfficePreviewConversionOutput> Failure(string code, string message) =>
        Result<DocumentOfficePreviewConversionOutput>.Failure(new Error(code, message, ErrorType.BusinessRule));
}
