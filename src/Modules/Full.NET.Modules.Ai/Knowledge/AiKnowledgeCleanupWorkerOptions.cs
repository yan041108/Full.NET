namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>删除恢复独立于索引生成开关，默认关闭，由 Worker 显式开启。</summary>
internal sealed class AiKnowledgeCleanupWorkerOptions
{
    internal const string SectionName = "FullNet:Ai:KnowledgeCleanupWorker";
    /// <summary>是否自动恢复已提交的删除意图。</summary>
    public bool Enabled { get; set; }
    /// <summary>每轮最多处理的任务或文件操作，范围 1–32。</summary>
    public int BatchSize { get; set; } = 4;
    /// <summary>每轮结束均等待，范围 250–60000 毫秒。</summary>
    public int PollMilliseconds { get; set; } = 1000;
    /// <summary>独立启用旧代次清理；默认不改变删除恢复的行为。</summary>
    public bool RetiredGenerationCleanupEnabled { get; set; }
    /// <summary>当前活动指针最近切换后的保留小时数，范围 24–8760；清理后不能原样回退到已删载荷。</summary>
    public int RetiredGenerationRetentionHours { get; set; } = 168;
}
