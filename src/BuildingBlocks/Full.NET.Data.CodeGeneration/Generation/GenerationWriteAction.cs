namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 标识安全写盘计划中单个生成产物的处理方式。
/// </summary>
public enum GenerationWriteActionKind
{
    /// <summary>新产物路径在上一版清单未拥有，且当前磁盘不存在；可直接创建。</summary>
    Create = 1,

    /// <summary>当前磁盘内容与上一版清单摘要一致，可安全替换为期望文本。</summary>
    Update = 2,

    /// <summary>期望文本与当前磁盘逐字一致，无需写盘但仍纳入清单。</summary>
    Unchanged = 3,

    /// <summary>当前磁盘内容与上一版清单不一致，禁止覆盖用户修改；计划不可整体应用。</summary>
    Conflict = 4,

    /// <summary>上一版清单拥有的产物当前摘要仍一致，可安全删除并保留 recovery 证据。</summary>
    Delete = 5,
}

/// <summary>
/// 描述一个尚未执行的生成产物写盘动作。
/// </summary>
/// <remarks>
/// 并发与原子性：该 record 只是单个动作的计划描述，不代表写盘已完成。
/// 调用方必须通过 <see cref="GenerationWritePlan"/> 整体协调：所有动作要么
/// 全部应用，要么检测到 Conflict 后整体 FAIL-closed 不写盘，禁止半写状态。
/// </remarks>
/// <param name="RelativePath">
/// 工作区相对路径；必须已通过 <see cref="GenerationArtifactPath.Validate"/>
/// 可移植性校验，不允许绝对路径或路径穿越（../）。
/// </param>
/// <param name="Kind">
/// 经上一版清单与当前磁盘内容双向所有权校验后的动作类型。
/// Conflict 时调用方不得静默降级，必须中止写计划并提示用户解决冲突。
/// </param>
/// <param name="Content">
/// 生成器期望写入的完整文本；删除动作和无变化动作为 null。
/// 非空时必须与 <paramref name="DesiredSha256"/> 严格一致，防止写盘前被篡改。
/// </param>
/// <param name="ExistingSha256">
/// 当前文件的 SHA-256 摘要；文件不存在时为 null。
/// Update/Delete 动作要求该值等于上一版清单摘要，否则标记为 Conflict 禁止覆盖用户修改。
/// </param>
/// <param name="DesiredSha256">
/// 期望文本的 SHA-256 摘要；删除动作为 null。
/// 用于在实际写盘前再次比对 Content，防止中间态内存修改导致哈希漂移。
/// </param>
public sealed record GenerationWriteAction(
    string RelativePath,
    GenerationWriteActionKind Kind,
    string? Content,
    string? ExistingSha256,
    string? DesiredSha256);
