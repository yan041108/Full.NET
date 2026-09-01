namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// 承载 Host 代码生成运行的不可变摘要，不保存 Schema、生成源码或异常正文。
/// 确定性哈希：SchemaSha256 与 ManifestSha256 均来自 GenerationContentHash 的 SHA-256 计算；摘要漂移立即 FAIL-closed 拒绝 apply/rollback。
/// </summary>
internal sealed class CodeGenerationRunRecord
{
    /// <summary>代码生成运行唯一标识；等于 Apply/Preview/Rollback 的 ApplyRunId，用于关联回滚检查点与产物下载。</summary>
    public Guid Id { get; init; }

    /// <summary>基于模板启动时指向 CodeGenerationTemplateRecord.Id；从目录直接预览或 apply 时为空。</summary>
    public Guid? TemplateId { get; init; }

    /// <summary>模板乐观并发版本号；空 Id 时为空，用于校验编辑页未被并发覆盖。</summary>
    public long? TemplateVersion { get; init; }

    /// <summary>运行类别稳定码：Preview、Apply 或 Rollback；用于列表过滤与 UI 展示区分。</summary>
    public string OperationKind { get; init; } = string.Empty;

    /// <summary>运行状态稳定码：Succeeded、Failed 或 RollbackSucceeded；任何中途异常立即落为 Failed。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>本次生成目标的模块键；目录浏览与批量预览时为空，用于按模块过滤历史列表。</summary>
    public string? ModuleKey { get; init; }

    /// <summary>本次生成目标的实体键；目录浏览与批量预览时为空，用于按实体查找最近一次成功 apply。</summary>
    public string? EntityKey { get; init; }

    /// <summary>输入 FullNetCrudSchema 的规范 JSON 摘要；相同 Schema 重新生成必须得到相同哈希，用于缓存与重放检测。</summary>
    public string? SchemaSha256 { get; init; }

    /// <summary>本次生成期望写出的产物数量；用于验收下载包与 apply 阶段产物数目一致。</summary>
    public int ArtifactCount { get; init; }

    /// <summary>成功 apply 时写入磁盘清单的摘要；回滚前必须与磁盘 Manifest 重算一致，FAIL-closed 禁止逆向。</summary>
    public string? ManifestSha256 { get; init; }

    /// <summary>失败时写入的稳定错误码；空值表示成功。错误码对应 CodeGenerationErrorCodes，不含用户消息。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>发起本次运行的用户 Id；用于审计、权限校验与回滚资格判定。</summary>
    public Guid RequestedByUserId { get; init; }

    /// <summary>运行进入编排阶段的 UTC 时间戳；与机器本地时区无关，用于排序与冷却期计算。</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>运行完成（成功或失败）的 UTC 时间戳；与 StartedAtUtc 一起用于计算耗时与保留冷却期。</summary>
    public DateTimeOffset FinishedAtUtc { get; init; }

    /// <summary>
    /// 回滚运行指向的成功 Apply Id；preview/apply 必须为空。
    /// </summary>
    public Guid? SourceApplyRunId { get; init; }
}