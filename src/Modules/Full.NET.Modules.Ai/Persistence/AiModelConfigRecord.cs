namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI 模型配置持久化行。</summary>
internal sealed class AiModelConfigRecord
{
    /// <summary>配置标识。</summary>
    public Guid Id { get; init; }

    /// <summary>租户标识；为空表示 Host 级配置。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>显示名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>提供程序键。</summary>
    public string ProviderKey { get; init; } = string.Empty;

    /// <summary>端点基址。</summary>
    public string EndpointBaseUrl { get; init; } = string.Empty;

    /// <summary>模型标识。</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>受保护的 API 密钥。</summary>
    public string? ApiKeyProtected { get; init; }

    /// <summary>OpenAI 组织标识。</summary>
    public string? OrganizationId { get; init; }

    /// <summary>是否为默认模型。</summary>
    public bool IsDefault { get; init; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>最近测试时间。</summary>
    public DateTimeOffset? LastTestedAtUtc { get; init; }

    /// <summary>最近测试状态键。</summary>
    public string? LastTestStatusKey { get; init; }

    /// <summary>最近测试摘要。</summary>
    public string? LastTestMessage { get; init; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>更新时间。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>乐观并发版本。</summary>
    public int Version { get; init; }
}
