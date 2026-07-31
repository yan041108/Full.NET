namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 标识安全写盘计划中单个生成产物的处理方式。
/// </summary>
public enum GenerationWriteActionKind
{
    Create = 1,
    Update = 2,
    Unchanged = 3,
    Conflict = 4,
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
