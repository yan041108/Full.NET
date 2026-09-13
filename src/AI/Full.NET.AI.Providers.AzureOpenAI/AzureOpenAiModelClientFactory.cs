using Full.NET.AI.Abstractions.Credentials;
using Full.NET.AI.Abstractions.Connectivity;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Providers.Http;
using Full.NET.AI.Providers.Internal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;

namespace Full.NET.AI.Providers.AzureOpenAI;

/// <summary>为 Azure OpenAI 创建隔离认证状态的中立客户端；ModelId 为 deployment 名称。</summary>
public sealed class AzureOpenAiModelClientFactory(
    IHttpClientFactory httpClientFactory,
    IDataProtectionProvider dataProtection,
    IProtectedModelCredentialStore credentials) : IAiModelClientFactory, IAiModelConnectivityProbe
{
    /// <inheritdoc/>
    public string ProviderKey => "azure_openai";

    /// <inheritdoc/>
    public async ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        var protectedCredential = await credentials.ReadAsync(binding, cancellationToken).ConfigureAwait(false);
        var key = string.IsNullOrWhiteSpace(protectedCredential) ? null :
            dataProtection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Unprotect(protectedCredential);
        return new HttpChatClient(
            httpClientFactory.CreateClient(AiProviderHttpClients.Name(ProviderKey, false)),
            binding,
            key);
    }

    /// <inheritdoc/>
    public async ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        ModelBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        var protectedCredential = await credentials.ReadAsync(binding, cancellationToken).ConfigureAwait(false);
        var key = string.IsNullOrWhiteSpace(protectedCredential) ? null :
            dataProtection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Unprotect(protectedCredential);
        var client = httpClientFactory.CreateClient(AiProviderHttpClients.Name(ProviderKey, false, embedding: true));
        return new HttpEmbeddingClient(binding, key, new ProviderEmbeddingTransport(client));
    }

    /// <inheritdoc/>
    public Task<ModelConnectivityResult> TestConnectivityAsync(ModelBinding binding, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        return AzureConnectivityProbe.TestAsync(httpClientFactory, dataProtection, credentials, binding, cancellationToken);
    }
}
