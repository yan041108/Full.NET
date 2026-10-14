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
public sealed class OllamaModelClientFactory(IHttpClientFactory httpClientFactory, IDataProtectionProvider dataProtection, IProtectedModelCredentialStore credentials, AiDataProcessingPolicy? dataPolicy = null, AiChatInputAdmissionPolicy? inputPolicy = null) : IAiModelClientFactory, IAiModelConnectivityProbe
{
    /// <summary>保留旧构造 ABI；新宿主使用完整冻结策略构造。</summary>
    /// <param name="httpClientFactory">受控工厂。</param><param name="dataProtection">凭据保护。</param>
    /// <param name="credentials">请求凭据范围。</param>
    public OllamaModelClientFactory(IHttpClientFactory httpClientFactory, IDataProtectionProvider dataProtection,
        IProtectedModelCredentialStore credentials) : this(httpClientFactory, dataProtection, credentials, null, null) { }
    /// <summary>
    /// Ollama 提供程序的唯一静态键，固定为 ollama，
    /// 用于匹配 ModelBinding 与 HTTP 客户端命名。
    /// </summary>
    public string ProviderKey => "ollama";
    /// <inheritdoc/>
    public async ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        (inputPolicy ?? AiChatInputAdmissionPolicy.Default).RequireChatDispatch();
        var clientName = AiProviderHttpClients.NameForData(ProviderKey, "chat", binding.DataClassification, dataPolicy);
        // 保留旧 purpose；明文只在 Provider 内用于当前请求，不能进入业务编排或摘要。
        var protectedCredential = await credentials.ReadAsync(binding, cancellationToken).ConfigureAwait(false);
        var key = string.IsNullOrWhiteSpace(protectedCredential) ? null :
            dataProtection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Unprotect(protectedCredential);
        return new HttpChatClient(
            httpClientFactory.CreateClient(clientName), binding, key);
    }
    /// <inheritdoc/>
    public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        ModelBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        var clientName = AiProviderHttpClients.NameForData(ProviderKey, "embedding", binding.DataClassification, dataPolicy);
        var client = httpClientFactory.CreateClient(clientName);
        return ValueTask.FromResult<IEmbeddingGenerator<string, Embedding<float>>>(
            new HttpEmbeddingClient(binding, null, new ProviderEmbeddingTransport(client)));
    }

    /// <inheritdoc/>
    public Task<ModelConnectivityResult> TestConnectivityAsync(ModelBinding binding, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        return ProviderConnectivityProbe.TestAsync(httpClientFactory, dataProtection, credentials, binding, false, cancellationToken, dataPolicy);
    }
}
