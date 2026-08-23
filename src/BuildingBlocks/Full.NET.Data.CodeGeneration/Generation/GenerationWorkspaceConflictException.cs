namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 表示磁盘工作区状态已偏离生成计划，调用方必须重新捕获并规划。
/// </summary>
public sealed class GenerationWorkspaceConflictException : IOException
{
    /// <summary>
    /// 初始化冲突异常；调用方捕获后必须重新捕获工作区快照并重新规划。
    /// </summary>
    /// <param name="message">说明冲突原因的消息文本。</param>
    /// <param name="relativePath">发生冲突的工作区相对路径；根目录级冲突时为空。</param>
    /// <param name="innerException">触发冲突的底层异常；无则为空。</param>
    public GenerationWorkspaceConflictException(
        string message,
        string? relativePath = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        RelativePath = relativePath;
    }

    /// <summary>
    /// 获取发生冲突的工作区相对路径；根目录级冲突时为空。
    /// </summary>
    public string? RelativePath { get; }
}
