using System.Text.Json;
using Full.NET.AI.Abstractions.Connectivity;
using Full.NET.AI.Abstractions.Credentials;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Providers.Http;
using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.AI.Providers.AzureOpenAI;

/// <summary>Azure OpenAI 目录探测；deployment 可能不在列表中，沿用人工核对语义。</summary>
internal static class AzureConnectivityProbe
{
    internal static async Task<ModelConnectivityResult> TestAsync(
        IHttpClientFactory http,
        IDataProtectionProvider protection,
        IProtectedModelCredentialStore credentials,
        ModelBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var protectedCredential = await credentials.ReadAsync(binding, budget.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(protectedCredential))
                return new(false, "API key is required for azure_openai provider.");
            var key = protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Unprotect(protectedCredential);
            if (string.IsNullOrWhiteSpace(key))
                return new(false, "API key is required for azure_openai provider.");

            var apiVersion = ResolveApiVersion(binding);
            using var client = http.CreateClient(AiProviderHttpClients.Name(binding.ProviderKey, true));
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{binding.Endpoint.AbsoluteUri.TrimEnd('/')}/openai/models?api-version={apiVersion}");
            request.Headers.TryAddWithoutValidation("api-key", key);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, $"AI provider rejected the request (HTTP {(int)response.StatusCode}).");

            var bytes = await ReadBoundedBodyAsync(response.Content, budget.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("data", out var catalog)
                || catalog.ValueKind != JsonValueKind.Array)
                return new(false, "AI provider returned an invalid model catalog.");

            var found = false;
            foreach (var item in catalog.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.String)
                    return new(false, "AI provider returned an invalid model catalog.");
                found |= string.Equals(id.GetString(), binding.ModelId, StringComparison.Ordinal);
            }

            return found
                ? new(true, "Connected successfully and deployment is listed by Azure OpenAI.")
                : new(true, "Connected successfully. Deployment was not found in provider catalog; verify deployment name manually.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return new(false, "AI connectivity test failed.");
        }
    }

    internal static string ResolveApiVersion(ModelBinding binding) =>
        binding.Options?.GetValueOrDefault("api_version")?.Trim() is { Length: > 0 } version
            ? version
            : "2024-10-21";

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
