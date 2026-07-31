namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// 承载 Host 代码生成模板的持久化投影，不向 HTTP 契约暴露存储字段。
/// </summary>
internal sealed class CodeGenerationTemplateRecord
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string SchemaJson { get; init; } = string.Empty;

    public string SchemaSha256 { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedByUserId { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public Guid? UpdatedByUserId { get; init; }

    public long Version { get; init; }
}
