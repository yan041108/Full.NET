namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// 承载 Host 代码生成运行的不可变摘要，不保存 Schema、生成源码或异常正文。
/// </summary>
internal sealed class CodeGenerationRunRecord
{
    public Guid Id { get; init; }

    public Guid? TemplateId { get; init; }

    public long? TemplateVersion { get; init; }

    public string OperationKind { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? ModuleKey { get; init; }

    public string? EntityKey { get; init; }

    public string? SchemaSha256 { get; init; }

    public int ArtifactCount { get; init; }

    public string? ManifestSha256 { get; init; }

    public string? ErrorCode { get; init; }

    public Guid RequestedByUserId { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset FinishedAtUtc { get; init; }

    /// <summary>
    /// 回滚运行指向的成功 Apply Id；preview/apply 必须为空。
    /// </summary>
    public Guid? SourceApplyRunId { get; init; }
}