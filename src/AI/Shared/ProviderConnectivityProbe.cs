using Full.NET.AI.Abstractions.Credentials;
using Full.NET.AI.Providers.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.AI.Abstractions.Connectivity;
using Full.NET.AI.Abstractions.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.AI.Providers.Internal;

/// <summary>两个适配器共享有界接收与请求生命周期；协议字段仅由适配器静态指定。</summary>
internal static class ProviderConnectivityProbe
{
    internal static async Task<ModelConnectivityResult> TestAsync(IHttpClientFactory http,
        IDataProtectionProvider protection, IProtectedModelCredentialStore credentials, ModelBinding binding, bool openAi, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            // 保持旧密文 purpose；明文局限于本次 Provider 请求，不写共享默认请求头。
            string? key = null;
            if (openAi)
            {
                var protectedCredential = await credentials.ReadAsync(binding, budget.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(protectedCredential))
                    return new(false, "API key is required for openai_compatible provider.");
                key = protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Unprotect(protectedCredential);
                if (string.IsNullOrWhiteSpace(key))
                    return new(false, "API key is required for openai_compatible provider.");
            }

            using var client = http.CreateClient(AiProviderHttpClients.Name(binding.ProviderKey, true));
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{binding.Endpoint.AbsoluteUri.TrimEnd('/')}/{(openAi ? "models" : "api/tags")}");
            if (openAi)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                var organization = binding.Options?.GetValueOrDefault("organization_id");
                if (!string.IsNullOrWhiteSpace(organization))
                    request.Headers.TryAddWithoutValidation("OpenAI-Organization", organization.Trim());
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, $"AI provider rejected the request (HTTP {(int)response.StatusCode}).");

            var bytes = await ReadBoundedBodyAsync(response.Content, budget.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(openAi ? "data" : "models", out var catalog)
                || catalog.ValueKind != JsonValueKind.Array)
                return new(false, "AI provider returned an invalid model catalog.");

            var found = false;
            foreach (var item in catalog.EnumerateArray())
            {
                // 任意文本、前缀或其他协议的字段不能充当模型存在证据。
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty(openAi ? "id" : "name", out var id)
                    || id.ValueKind != JsonValueKind.String)
                    return new(false, "AI provider returned an invalid model catalog.");
                found |= string.Equals(id.GetString(), binding.ModelId, StringComparison.Ordinal);
            }

            budget.Token.ThrowIfCancellationRequested();
            if (found)
                return new(true, openAi ? "Connected successfully and model is listed by provider."
                    : "Connected successfully and model is available in Ollama.");
            // OpenAI 兼容网关可能隐藏目录项，沿用告知人工核对的成功语义；Ollama 要求已安装模型。
            return openAi
                ? new(true, "Connected successfully. Model was not found in provider catalog; verify model id manually.")
                : new(false, "Connected to Ollama but configured model was not found in /api/tags.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return new(false, "AI connectivity test failed.");
        }
    }

    /// <summary>最多读取 64 KiB 加一个越界哨兵字节；超限不解析，流与客户端始终释放。</summary>
    private static async Task<ReadOnlyMemory<byte>> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var bytes = new byte[65537];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0) return bytes.AsMemory(0, length);
            length += read;
        }
        throw new InvalidDataException("AI model catalog exceeds the response limit.");
    }
}
