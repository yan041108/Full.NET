using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Ai.Diagnostics;

/// <summary>AI 故障阶段白名单；调用方不得以用户输入构造诊断分组。</summary>
internal enum AiFailureStage
{
    AgentRun, AgentRunIteration, AgentRejection, AgentCompletion, AgentLease, AgentBudgetRepair,
    AgentApprovalCommit, AgentReconciliationCommit, AgentWorkflowCommit,
    ChatLease, ChatCleanup, KnowledgeIndex, KnowledgeIndexIteration,
    KnowledgeCleanupIndex, KnowledgeCleanupImport, KnowledgeCleanupRetired, KnowledgeCleanupIteration,
}

/// <summary>只投影闭合故障分类；诊断设施失效不能改变取消、清理或预算状态。</summary>
internal static class AiFailureLog
{
    private static readonly ReadOnlyCollection<KeyValuePair<string, object?>> Classification = Array.AsReadOnly<KeyValuePair<string, object?>>(
    [
        new("log.class", LogClassification.Diagnostic), new("reliability.class", "BestEffort"),
        new("data.classification", "Internal"), new("DiagnosticGroup", "ai.operations"),
    ]);

    /// <summary>原异常只参与类型模式判断，不交给日志格式器、Scope 或 Sink。</summary>
    internal static void Write(ILogger logger, LogLevel level, AiFailureStage stage, Exception? failure, Guid? operationId = null)
    {
        try
        {
            if (!logger.IsEnabled(level)) return;
            using var scope = logger.BeginScope(Classification);
            var state = new FailureState(StageKey(stage), FailureCategory(stage, failure),
                operationId == Guid.Empty ? null : operationId,
                stage == AiFailureStage.KnowledgeIndex ? "ai.knowledge.index_unavailable" : null);
            // 诊断提供程序可能抛错或重入；每次调用独立持有状态，不复用源生成器的线程局部 TagArray。
            logger.Log(level, new EventId(5601, "AiOperationFailed"), state, null,
                static (s, _) => string.Create(CultureInfo.InvariantCulture,
                    $"AI operation failed at {s.Stage}; category {s.FailureCategory}, operation {s.OperationId}, code {s.FailureCode}."));
        }
        catch (Exception)
        {
            // 只隔离诊断调用（含启用探测、Scope 与释放），业务状态收敛由调用方继续执行。
        }
    }

    private static string FailureCategory(AiFailureStage stage, Exception? failure) => failure switch
    {
        OperationCanceledException => "cancelled", TimeoutException => "timeout",
        UnauthorizedAccessException => "authorization", AiBudgetException or AiRunOutputBudgetException => "budget",
        HttpRequestException => "provider", InvalidDataException or ArgumentException => "invalid",
        IOException => "storage",
        null when stage is AiFailureStage.AgentApprovalCommit or AiFailureStage.AgentReconciliationCommit or AiFailureStage.AgentWorkflowCommit => "lease_lost",
        _ => "other",
    };

    private static string StageKey(AiFailureStage stage) => stage switch
    {
        AiFailureStage.AgentRun => "agent_run", AiFailureStage.AgentRunIteration => "agent_run_iteration",
        AiFailureStage.AgentRejection => "agent_rejection", AiFailureStage.AgentCompletion => "agent_completion",
        AiFailureStage.AgentLease => "agent_lease", AiFailureStage.AgentBudgetRepair => "agent_budget_repair",
        AiFailureStage.AgentApprovalCommit => "agent_approval_commit", AiFailureStage.AgentReconciliationCommit => "agent_reconciliation_commit",
        AiFailureStage.AgentWorkflowCommit => "agent_workflow_commit", AiFailureStage.ChatLease => "chat_lease",
        AiFailureStage.ChatCleanup => "chat_cleanup", AiFailureStage.KnowledgeIndex => "knowledge_index",
        AiFailureStage.KnowledgeIndexIteration => "knowledge_index_iteration", AiFailureStage.KnowledgeCleanupIndex => "knowledge_cleanup_index",
        AiFailureStage.KnowledgeCleanupImport => "knowledge_cleanup_import", AiFailureStage.KnowledgeCleanupRetired => "knowledge_cleanup_retired",
        AiFailureStage.KnowledgeCleanupIteration => "knowledge_cleanup_iteration", _ => "other",
    };

    /// <summary>固定字段的不可变投影；静态格式器与有界枚举保持 AOT 闭包和重入隔离。</summary>
    private readonly record struct FailureState(string Stage, string FailureCategory, Guid? OperationId, string? FailureCode)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => 5;
        public KeyValuePair<string, object?> this[int index] => index switch
        {
            0 => new("Stage", Stage), 1 => new("FailureCategory", FailureCategory),
            2 => new("OperationId", OperationId), 3 => new("FailureCode", FailureCode),
            4 => new("{OriginalFormat}", "AI operation failed at {Stage}; category {FailureCategory}, operation {OperationId}, code {FailureCode}."),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var index = 0; index < Count; index++) yield return this[index];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
