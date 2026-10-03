namespace Full.NET.Modules.Ai.Persistence;

/// <summary>私有知识库目录行；不保存文档内容或模型凭据。</summary>
internal sealed class AiKnowledgeBaseRecord
{
    /// <summary>逻辑主键。</summary>
    public Guid Id { get; init; }
    /// <summary>所属租户，Host 为 null。</summary>
    public Guid? TenantId { get; init; }
    /// <summary>可信所有者。</summary>
    public Guid OwnerUserId { get; init; }
    /// <summary>目录名称。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>目录描述。</summary>
    public string? Description { get; init; }
    /// <summary>是否允许后续处理。</summary>
    public bool IsEnabled { get; init; }
    /// <summary>稳定数据分类码。</summary>
    public string DataClassification { get; init; } = string.Empty;
    /// <summary>获批向量模型。</summary>
    public Guid? EmbeddingModelConfigId { get; init; }
    /// <summary>获批向量配置版本。</summary>
    public int? EmbeddingModelVersion { get; init; }
    /// <summary>获批生成模型。</summary>
    public Guid? GenerationModelConfigId { get; init; }
    /// <summary>获批生成配置版本。</summary>
    public int? GenerationModelVersion { get; init; }
    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
    /// <summary>更新时间。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    /// <summary>目录与审批共享乐观版本。</summary>
    public int Version { get; init; }
}
