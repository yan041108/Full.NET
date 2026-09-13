using System.Collections.Frozen;
using Full.NET.AI.Abstractions.Credentials;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Security;

/// <summary>只在已授权的请求作用域内签发短期凭据引用；不得持久化或跨请求复用。</summary>
internal sealed class AiModelBindingScope : IProtectedModelCredentialStore, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, (ModelBinding Binding, string Protected)> credentials = [];
    private bool disposed;

    /// <summary>调用方须先按聊天或管理权限查询配置；冻结选项，防止签发后更改凭据用途。</summary>
    internal ModelBinding Create(AiModelConfigRecord model)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // 随机值是短期能力引用而非数据库逻辑主键，不从配置 ID 或租户 ID 推导。
            Guid? reference = string.IsNullOrWhiteSpace(model.ApiKeyProtected) ? null : Guid.NewGuid();
            var options = BuildOptions(model);
            var binding = new ModelBinding(model.Id, model.Version, model.ProviderKey, model.ModelId,
                new Uri(model.EndpointBaseUrl), reference, options);
            if (reference.HasValue) credentials.Add(reference.Value, (binding, model.ApiKeyProtected!));
            return binding;
        }
    }

    public ValueTask<string?> ReadAsync(ModelBinding binding, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!binding.CredentialReference.HasValue) return ValueTask.FromResult<string?>(null);
            // Uri 值相等会忽略用户信息和片段；凭据用途必须额外比较完整绝对地址。
            if (!credentials.TryGetValue(binding.CredentialReference.Value, out var entry) || entry.Binding != binding
                || !string.Equals(entry.Binding.Endpoint.AbsoluteUri, binding.Endpoint.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidOperationException("AI credential reference is not valid for this binding.");
            return ValueTask.FromResult<string?>(entry.Protected);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            credentials.Clear();
        }
    }

    private static FrozenDictionary<string, string>? BuildOptions(AiModelConfigRecord model)
    {
        if (string.IsNullOrWhiteSpace(model.OrganizationId))
            return null;
        var optionKey = string.Equals(model.ProviderKey, "azure_openai", StringComparison.Ordinal)
            ? "api_version"
            : "organization_id";
        return new Dictionary<string, string> { [optionKey] = model.OrganizationId.Trim() }.ToFrozenDictionary();
    }
}
