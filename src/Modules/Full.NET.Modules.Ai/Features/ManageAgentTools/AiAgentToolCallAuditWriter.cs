using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>写入 Agent Tool 调用审计记录。</summary>
internal sealed class AiAgentToolCallAuditWriter(
    ICommandExecutor commandExecutor,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
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
}
