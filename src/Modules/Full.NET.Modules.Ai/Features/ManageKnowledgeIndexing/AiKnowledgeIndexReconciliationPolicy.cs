namespace Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;

/// <summary>只读恢复分类；提示不会派发模型或改变旧任务、原会话与收费凭据。</summary>
internal static class AiKnowledgeIndexReconciliationPolicy
{
    internal static (string Reason, string Action) Decide(string state, string? taskState, bool sourceAvailable,
        bool bindingCurrent, bool formatSupported, int attempts, DateTimeOffset? leaseExpires, DateTimeOffset now, bool taskAuthorityCurrent = true)
    {
        if (state == "deleted") return ("deleted", "none");
        if (state == "deleting") return ("cleanup_pending", "wait");
        // 运行时开关只禁止新处理；既有完整代次仍按独立读取证明报告就绪。
        if (state == "ready" && taskState == "completed") return ("ready", "none");
        if (!formatSupported) return ("format_unsupported", "none");
        if (!sourceAvailable) return ("source_unavailable", "none");
        if (taskState is null) return ("not_indexed", "enqueue");
        if (!bindingCurrent) return ("binding_stale", "enqueue");
        if (!taskAuthorityCurrent) return ("generation_unavailable", "rebuild");
        return taskState switch
        {
            "queued" => ("queued", "wait"),
            "running" when leaseExpires is null || leaseExpires <= now => attempts >= 3
                ? ("lease_exhausted", "retry") : ("lease_expired", "wait"),
            "running" => ("processing", "wait"),
            "failed" => ("task_failed", "retry"),
            "cancelled" => ("task_cancelled", "rebuild"),
            _ => ("generation_unavailable", "rebuild"),
        };
    }
}
