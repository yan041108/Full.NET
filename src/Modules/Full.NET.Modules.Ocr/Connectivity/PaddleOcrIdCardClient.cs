using System.Net.Http.Headers;
using System.Text;
using Full.NET.Modules.Ocr.Domain;
using Full.NET.Modules.Ocr.Persistence;

namespace Full.NET.Modules.Ocr.Connectivity;

/// <summary>PaddleOCR 身份证识别 HTTP 适配器。</summary>
internal sealed class PaddleOcrIdCardClient : IPaddleOcrIdCardClient
{
    public const string HttpClientName = "Full.NET.Ocr.PaddleIdCard";

    /// <summary>将图片发送到 Provider 并解析身份证字段。</summary>
    /// <param name="config">Provider 配置。</param>
    /// <param name="apiKey">已解保护的 API 密钥；未配置时为空。</param>
    /// <param name="content">源图片流；调用方负责释放。</param>
    /// <param name="contentType">内容类型。</param>
    /// <param name="fileName">原始文件名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>识别结果；网络超时向外抛出，供任务服务按未知状态收敛。</returns>
    public async Task<(bool Succeeded, Domain.OcrIdCardParsedResult? Result, string RawJson, string Message)> RecognizeAsync(
        OcrProviderConfigRecord config,
        string? apiKey,
        Stream content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(config.BaseUrl))
        {
            Content = form,
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var rawJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, Truncate(rawJson), $"HTTP {(int)response.StatusCode}: {rawJson}");
            }

            if (!Domain.OcrIdCardResponseParser.TryParse(rawJson, out var parsed, out var message))
            {
                return (false, null, Truncate(rawJson), message);
            }

            return (true, parsed, Truncate(rawJson), message);
        }
        catch (Exception ex) when (!UnknownExternalSideEffect.Matches(ex))
        {
            return (false, null, string.Empty, ex.Message);
        }
    }

    /// <summary>执行轻量连通性探测。</summary>
    public async Task<(bool Succeeded, string Message)> TestAsync(
        OcrProviderConfigRecord config,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildHealthEndpoint(config.BaseUrl));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (true, "Provider health endpoint responded successfully.");
            }

            return (false, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string BuildEndpoint(string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}/api/ocr/id-card";

    private static string BuildHealthEndpoint(string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}/health";

    private static string Truncate(string value) =>
        value.Length <= Domain.OcrIdCardPolicy.MaxRawResultJsonLength
            ? value
            : value[..Domain.OcrIdCardPolicy.MaxRawResultJsonLength];
}
