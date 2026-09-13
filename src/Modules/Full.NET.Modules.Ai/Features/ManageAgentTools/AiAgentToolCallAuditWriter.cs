using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Approvals;
using System.Globalization;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>写入 Agent Tool 调用审计记录。</summary>
internal sealed class AiAgentToolCallAuditWriter(
    ICommandExecutor commandExecutor,
    IAgentToolApprovalBindingReader approvals,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>调用主键即 OperationId，跨实例重复插入由主键拒绝；不自动重放执行。</summary>
    internal async Task BeginOperationAsync(Guid actorUserId, ToolInvocation invocation, string permissionCode,
        string statusKey, string? errorCode, string? traceId, CancellationToken cancellationToken)
    {
        var hash = AgentApprovalGate.ComputeArgumentsHash(invocation.Arguments);
        var binding = await approvals.FindBindingByOperationAsync(invocation.OperationId, cancellationToken).ConfigureAwait(false);
        // 摘要只包含由服务端生成的固定字段，不再尝试从任意文本提取敏感值。
        var summary = "argumentsSha256=" + hash + ";toolVersion=" + invocation.ToolVersion.ToString(CultureInfo.InvariantCulture);
        var operationId = invocation.OperationId == Guid.Empty ? idGenerator.NewId() : invocation.OperationId;
        var scopeTenantId = AiChatScope.Resolve(currentTenant).TenantId;
        var parameters = AiSqlParameters.Create(
            ("Id", operationId),
            ("TenantId", scopeTenantId), ("ActorUserId", actorUserId),
            ("ToolName", AiAgentToolCatalog.Find(invocation.ToolName ?? string.Empty)?.ToolName ?? "unknown"),
            ("PermissionCode", permissionCode), ("StatusKey", statusKey), ("DurationMs", null),
            ("InputSummary", summary), ("OutputSummary", null), ("ErrorCode", errorCode),
            ("TraceId", traceId), ("RunId", invocation.RunId), ("StepId", null),
            ("ArgumentsHash", hash), ("ApprovalId", binding?.Id), ("ReceiptId", null),
            ("CreatedAtUtc", clock.UtcNow));
        var affected = await TryInsertCallAsync(parameters, cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            affected = await RecoverExistingCallAsync(
                operationId,
                actorUserId,
                scopeTenantId,
                statusKey,
                summary,
                binding?.Id,
                cancellationToken).ConfigureAwait(false);
        }

        if (affected != 1) throw new InvalidOperationException("Tool audit intent was not persisted.");
    }

    /// <summary>只收敛本主体的 started 操作；结果写入失败必须向执行器传播。</summary>
    internal async Task CompleteOperationAsync(Guid actorUserId, Guid operationId, string statusKey, string? errorCode,
        int outputBytes, int durationMs, CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(AiToolExecutionSql.Complete, AiSqlParameters.Create(
            ("OperationId", operationId), ("ActorUserId", actorUserId), ("ScopeTenantId", AiChatScope.Resolve(currentTenant).TenantId),
            ("StatusKey", statusKey), ("ErrorCode", errorCode), ("DurationMs", durationMs),
            ("OutputSummary", "outputBytes=" + outputBytes.ToString(CultureInfo.InvariantCulture))), cancellationToken).ConfigureAwait(false);
        if (affected != 1) throw new InvalidOperationException("Tool audit receipt was not persisted.");
    }

    /// <summary>追加一条调用审计记录。</summary>
    /// <param name="actorUserId">调用用户标识。</param>
    /// <param name="toolName">工具名。</param>
    /// <param name="permissionCode">权限码。</param>
    /// <param name="statusKey">状态键。</param>
    /// <param name="durationMs">耗时毫秒。</param>
    /// <param name="inputSummary">输入摘要。</param>
    /// <param name="outputSummary">输出摘要。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="traceId">Trace 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task AppendAsync(
        Guid actorUserId,
        string toolName,
        string permissionCode,
        string statusKey,
        int? durationMs,
        string? inputSummary,
        string? outputSummary,
        string? errorCode,
        string? traceId,
        CancellationToken cancellationToken = default)
    {
        var scope = AiChatScope.Resolve(currentTenant);
        await commandExecutor.ExecuteAsync(
                AiAgentToolCallSql.InsertCall,
                AiSqlParameters.Create(
                [
                    ("Id", idGenerator.NewId()),
                    ("TenantId", scope.TenantId),
                    ("ActorUserId", actorUserId),
                    ("ToolName", toolName),
                    ("PermissionCode", permissionCode),
                    ("StatusKey", statusKey),
                    ("DurationMs", durationMs),
                    ("InputSummary", AiAgentToolAuditPolicy.Summarize(inputSummary)),
                    ("OutputSummary", outputSummary is null
                        ? null
                        : AiAgentToolAuditPolicy.Summarize(outputSummary)),
                    ("ErrorCode", errorCode),
                    ("TraceId", traceId),
                    ("CreatedAtUtc", clock.UtcNow),
                ]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> TryInsertCallAsync(
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            return await commandExecutor.ExecuteAsync(AiAgentToolCallSql.InsertCall, parameters, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return 0;
        }
    }

    private async Task<int> RecoverExistingCallAsync(
        Guid operationId,
        Guid actorUserId,
        Guid? scopeTenantId,
        string statusKey,
        string summary,
        Guid? approvalId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(statusKey, "started", StringComparison.Ordinal))
        {
            return await commandExecutor.ExecuteAsync(
                AiToolExecutionSql.TransitionDeniedApprovalToStarted,
                AiSqlParameters.Create(
                    ("OperationId", operationId),
                    ("ActorUserId", actorUserId),
                    ("ScopeTenantId", scopeTenantId),
                    ("InputSummary", summary),
                    ("ApprovalId", approvalId),
                    ("Now", clock.UtcNow)),
                cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(statusKey, "denied", StringComparison.Ordinal))
        {
            return 0;
        }

        // 审批拒绝或已消费后的重复拒绝：主键已存在即视为幂等审计。
        return await commandExecutor.ExecuteAsync(
            AiToolExecutionSql.TouchExistingCall,
            AiSqlParameters.Create(
                ("OperationId", operationId),
                ("ActorUserId", actorUserId),
                ("ScopeTenantId", scopeTenantId)),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is DataCommandException { Kind: DataCommandFailureKind.UniqueConstraint }
        || ex.Message.Contains("PRIMARY", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
}
