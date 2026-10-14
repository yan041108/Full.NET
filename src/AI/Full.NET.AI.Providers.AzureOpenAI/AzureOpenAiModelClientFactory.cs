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
    IProtectedModelCredentialStore credentials, AiDataProcessingPolicy? dataPolicy = null, AiChatInputAdmissionPolicy? inputPolicy = null) : IAiModelClientFactory, IAiModelConnectivityProbe
{
    /// <summary>保留旧构造 ABI；新宿主使用完整冻结策略构造。</summary>
    /// <param name="httpClientFactory">受控工厂。</param><param name="dataProtection">凭据保护。</param>
    /// <param name="credentials">请求凭据范围。</param>
    public AzureOpenAiModelClientFactory(IHttpClientFactory httpClientFactory, IDataProtectionProvider dataProtection,
        IProtectedModelCredentialStore credentials) : this(httpClientFactory, dataProtection, credentials, null, null) { }
    /// <summary>
    /// Azure OpenAI 提供程序的唯一静态键，固定为 azure_openai，
    /// 用于匹配 ModelBinding 与 HTTP 客户端命名。
    /// </summary>
    public string ProviderKey => "azure_openai";

    /// <inheritdoc/>
    public async ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        (inputPolicy ?? AiChatInputAdmissionPolicy.Default).RequireChatDispatch();
        var clientName = AiProviderHttpClients.NameForData(ProviderKey, "chat", binding.DataClassification, dataPolicy);
        var protectedCredential = await credentials.ReadAsync(binding, cancellationToken).ConfigureAwait(false);
        var key = string.IsNullOrWhiteSpace(protectedCredential) ? null :
            dataProtection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Unprotect(protectedCredential);
        return new HttpChatClient(
            httpClientFactory.CreateClient(clientName),
            binding,
            key);
    }

    /// <inheritdoc/>
    public async ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        ModelBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        var clientName = AiProviderHttpClients.NameForData(ProviderKey, "embedding", binding.DataClassification, dataPolicy);
        var protectedCredential = await credentials.ReadAsync(binding, cancellationToken).ConfigureAwait(false);
        var key = string.IsNullOrWhiteSpace(protectedCredential) ? null :
            dataProtection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Unprotect(protectedCredential);
        var client = httpClientFactory.CreateClient(clientName);
        return new HttpEmbeddingClient(binding, key, new ProviderEmbeddingTransport(client));
    }

    /// <inheritdoc/>
    public Task<ModelConnectivityResult> TestConnectivityAsync(ModelBinding binding, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.ProviderKey != ProviderKey) throw new InvalidOperationException("AI provider mismatch.");
        return AzureConnectivityProbe.TestAsync(httpClientFactory, dataProtection, credentials, binding, cancellationToken, dataPolicy);
    }
}
