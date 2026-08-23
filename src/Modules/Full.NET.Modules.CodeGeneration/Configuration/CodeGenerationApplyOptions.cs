namespace Full.NET.Modules.CodeGeneration.Configuration;

/// <summary>
/// 配置 Host 代码生成 Apply 使用的服务器本地工作区；默认禁用以避免宿主意外写盘。
/// </summary>
internal sealed class CodeGenerationApplyOptions
{
    public const string SectionName = "CodeGeneration:Apply";

    /// <summary>
    /// 是否启用服务器本地 Apply；默认关闭，启用后必须配置已存在的本地工作区目录，否则启动期校验失败。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Apply 与 Rollback 写盘使用的本地工作区根目录，必须是已存在的本地绝对路径，禁止 UNC 远程路径。
    /// </summary>
    public string WorkspaceRoot { get; set; } = string.Empty;

    /// <summary>
    /// 启用后通过数据库会话锁在多个 API 实例间串行化同一工作区的 Apply/Rollback。
    /// </summary>
    public bool DistributedGateEnabled { get; set; }

    /// <summary>
    /// 单次 rollback-chain 请求允许的最大 Apply 数量。
    /// </summary>
    public int MaxRollbackChainLength { get; set; } = 16;
}
