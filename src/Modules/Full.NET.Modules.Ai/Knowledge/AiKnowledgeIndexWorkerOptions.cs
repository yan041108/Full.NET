namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>索引 Worker 显式启用；租约和派发时限继续使用已验证执行边界。</summary>
internal sealed class AiKnowledgeIndexWorkerOptions
{
    internal const string SectionName = "FullNet:Ai:KnowledgeIndexWorker";
    /// <summary>只控制后台扫描，不声明 API 可接受索引或公开 ready。</summary>
    public bool Enabled { get; set; }
    /// <summary>每轮候选上限，范围 1–32。</summary>
    public int BatchSize { get; set; } = 4;
    /// <summary>未满批时的轮询间隔，范围 250–60000 毫秒。</summary>
    public int PollMilliseconds { get; set; } = 1000;
}
