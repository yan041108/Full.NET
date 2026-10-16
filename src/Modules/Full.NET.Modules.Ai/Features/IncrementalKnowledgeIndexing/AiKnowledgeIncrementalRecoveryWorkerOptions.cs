namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>Worker 的无费用意图补偿配置；默认关闭，修改后重启 Worker 生效。</summary>
internal sealed class AiKnowledgeIncrementalRecoveryWorkerOptions
{
    internal const string SectionName = "FullNet:Ai:KnowledgeIncrementalRecoveryWorker";
    /// <summary>是否自动补齐来源意图；不授权索引任务或模型派发。</summary>
    public bool Enabled { get; set; }
    /// <summary>每轮最多复核的文档数，范围1..32，默认4。</summary>
    public int BatchSize { get; set; } = 4;
    /// <summary>每轮结束后的等待毫秒数，范围1000..60000，默认10000；失败和满批也等待。</summary>
    public int PollMilliseconds { get; set; } = 10000;
}
