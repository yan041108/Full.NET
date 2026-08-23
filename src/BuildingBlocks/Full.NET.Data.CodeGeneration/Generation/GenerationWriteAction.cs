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
/// <param name="RelativePath">工作区相对路径。</param>
/// <param name="Kind">经所有权校验后的动作类型。</param>
/// <param name="Content">生成器期望写入的完整文本；删除动作没有期望文本。</param>
/// <param name="ExistingSha256">当前文件摘要；文件不存在时为空。</param>
/// <param name="DesiredSha256">期望文本摘要；删除动作为空。</param>
public sealed record GenerationWriteAction(
    string RelativePath,
    GenerationWriteActionKind Kind,
    string? Content,
    string? ExistingSha256,
    string? DesiredSha256);
