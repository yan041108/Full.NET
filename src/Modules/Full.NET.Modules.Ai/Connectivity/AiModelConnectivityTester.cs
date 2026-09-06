using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Connectivity;

/// <summary>在受控白名单提供程序上执行模型连通性探测。</summary>
internal sealed class AiModelConnectivityTester(IHttpClientFactory httpClientFactory)
{
    private const int TimeoutSeconds = 15;
    public const string HttpClientName = "Full.NET.Ai.ModelConnectivity";

    /// <summary>测试模型配置连通性。</summary>
    /// <param name="record">已持久化的配置行。</param>
    /// <param name="apiKey">解保护后的 API 密钥；Ollama 可为空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>测试结果消息。</returns>
    public async Task<(bool Succeeded, string Message)> TestAsync(
        AiModelConfigRecord record,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(record.ProviderKey, AiProviderKeys.OpenAiCompatible, StringComparison.Ordinal))
        {
            return await TestOpenAiCompatibleAsync(record, apiKey, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(record.ProviderKey, AiProviderKeys.Ollama, StringComparison.Ordinal))
        {
            return await TestOllamaAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return (false, "Unsupported provider key.");
    }

    private async Task<(bool Succeeded, string Message)> TestOpenAiCompatibleAsync(
        AiModelConfigRecord record,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, "API key is required for openai_compatible provider.");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{NormalizeBaseUrl(record.EndpointBaseUrl)}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (!string.IsNullOrWhiteSpace(record.OrganizationId))
        {
            request.Headers.TryAddWithoutValidation("OpenAI-Organization", record.OrganizationId.Trim());
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, SanitizeMessage($"HTTP {(int)response.StatusCode}: {body}"));
            }

            if (ContainsModelId(body, record.ModelId))
            {
                return (true, "Connected successfully and model is listed by provider.");
            }

            return (true, "Connected successfully. Model was not found in provider catalog; verify model id manually.");
        }
        catch (Exception ex)
        {
            return (false, SanitizeMessage(ex.Message));
        }
    }

    private async Task<(bool Succeeded, string Message)> TestOllamaAsync(
        AiModelConfigRecord record,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{NormalizeBaseUrl(record.EndpointBaseUrl)}/api/tags");

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, SanitizeMessage($"HTTP {(int)response.StatusCode}: {body}"));
            }

            if (ContainsModelId(body, record.ModelId))
            {
                return (true, "Connected successfully and model is available in Ollama.");
            }

            return (false, "Connected to Ollama but configured model was not found in /api/tags.");
        }
        catch (Exception ex)
        {
            return (false, SanitizeMessage(ex.Message));
        }
    }

    internal static string NormalizeBaseUrl(string endpointBaseUrl) =>
        endpointBaseUrl.Trim().TrimEnd('/');

    internal static bool ContainsModelId(string responseBody, string modelId)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id)
                        && string.Equals(id.GetString(), modelId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            if (document.RootElement.TryGetProperty("models", out var models)
                && models.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in models.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var name)
                        && string.Equals(name.GetString(), modelId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return responseBody.Contains(modelId, StringComparison.Ordinal);
        }

        return responseBody.Contains(modelId, StringComparison.Ordinal);
    }

    private static string SanitizeMessage(string message) =>
        message.Length <= 512 ? message : message[..512];
}
