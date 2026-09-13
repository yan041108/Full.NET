using Full.NET.AI.Abstractions.Credentials;
using Full.NET.AI.Providers.Http;
using Full.NET.AI.Abstractions.Connectivity;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Providers.Internal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
namespace Full.NET.AI.Providers.Ollama;
/// <summary>为 Ollama 创建隔离认证状态的中立客户端。</summary>
/// <param name="httpClientFactory">受宿主配置的 HTTP 客户端工厂。</param>
/// <param name="dataProtection">与历史配置兼容的凭据保护服务。</param>
/// <param name="credentials">请求作用域内的授权凭据读取服务。</param>
public sealed class OllamaModelClientFactory(IHttpClientFactory httpClientFactory, IDataProtectionProvider dataProtection, IProtectedModelCredentialStore credentials) : IAiModelClientFactory, IAiModelConnectivityProbe
{
    /// <inheritdoc/>
    public string ProviderKey => "ollama";
    /// <inheritdoc/>
    public async ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        // 保留旧 purpose；明文只在 Provider 内用于当前请求，不能进入业务编排或摘要。
        var protectedCredential = await credentials.ReadAsync(binding, cancellationToken).ConfigureAwait(false);
        var key = string.IsNullOrWhiteSpace(protectedCredential) ? null :
            dataProtection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Unprotect(protectedCredential);
        return new HttpChatClient(
            httpClientFactory.CreateClient(AiProviderHttpClients.Name(ProviderKey, false)), binding, key);
    }
    /// <inheritdoc/>
    public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        ModelBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        var client = httpClientFactory.CreateClient(AiProviderHttpClients.Name(ProviderKey, false, embedding: true));
        return ValueTask.FromResult<IEmbeddingGenerator<string, Embedding<float>>>(
            new HttpEmbeddingClient(binding, null, new ProviderEmbeddingTransport(client)));
    }

    /// <inheritdoc/>
    public Task<ModelConnectivityResult> TestConnectivityAsync(ModelBinding binding, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        return ProviderConnectivityProbe.TestAsync(httpClientFactory, dataProtection, credentials, binding, false, cancellationToken);
    }
}
