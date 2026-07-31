namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 表示磁盘工作区状态已偏离生成计划，调用方必须重新捕获并规划。
/// </summary>
public sealed class GenerationWorkspaceConflictException : IOException
{
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
