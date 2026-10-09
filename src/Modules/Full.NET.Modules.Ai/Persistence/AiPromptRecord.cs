namespace Full.NET.Modules.Ai.Persistence;

/// <summary>可信作用域内的发布指针；修订号串行化写入，Epoch 在停用时撤销旧运行。</summary>
internal sealed class AiPromptRecord
{
    public Guid Id { get; init; }
    public string ScopeKey { get; init; } = "";
    public Guid? TenantId { get; init; }
    public Guid OwnerUserId { get; init; }
    public Guid ClientRequestId { get; init; }
    public string CreateHash { get; init; } = "";
    public string Name { get; init; } = "";
    public int Revision { get; init; }
    public int LastVersion { get; init; }
    public Guid? ActiveVersionId { get; init; }
    public int Epoch { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>正文和模型身份只追加；WasPublished 仅从 false 转为 true，不改写正文。</summary>
internal sealed class AiPromptVersionRecord
{
    public Guid Id { get; init; }
    public Guid PromptId { get; init; }
    public string ScopeKey { get; init; } = "";
    public Guid? TenantId { get; init; }
    public int Version { get; init; }
    public int FormatVersion { get; init; }
    public string BodyJson { get; init; } = "";
    public int ModelConfigVersion { get; init; }
    public string ProviderKey { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string BodyHash { get; init; } = "";
    public bool WasPublished { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
