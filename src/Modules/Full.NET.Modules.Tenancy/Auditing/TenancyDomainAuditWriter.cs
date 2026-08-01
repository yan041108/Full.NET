using System.Diagnostics;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Auditing;

/// <summary>
/// Tenancy 模块 B0 域内审计写入器：把 <see cref="TenancyDomainAuditWrite"/> 写入
/// <c>fn_tenancy_domain_audit</c>。
/// </summary>
/// <remarks>
/// 本类型只使用调用方注入的 <see cref="ICommandExecutor"/> 执行单条 INSERT，
/// 不持有也不开启 <c>ICommandTransaction</c>——调用方必须已经处于业务写入所在的
/// <c>transaction.ExecuteAsync</c> 回调内部，这样 INSERT 会通过 Dapper 的 <c>DbSession</c>
/// 自动加入同一个环境事务，与业务写入同提交、同回滚。写入不经过 Outbox。
/// </remarks>
internal sealed class TenancyDomainAuditWriter(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    IClock clock) : ITransactionalDomainAuditWriter<TenancyDomainAuditWrite>
{
    public async Task WriteAsync(
        TenancyDomainAuditWrite auditWrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditWrite);

        // 使用环境 Activity 的 TraceId 而非请求取消令牌相关的上下文，
        // 使同一事务内跨模块写入的审计记录可以按 TraceId 关联到同一次请求。
        var traceId = Activity.Current?.TraceId.ToString();
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenancyDomainAuditSql.Insert,
                new
                {
                    Id = idGenerator.NewId(),
                    auditWrite.TenantId,
                    auditWrite.ActionKey,
                    auditWrite.EntityId,
                    auditWrite.Outcome,
                    auditWrite.ActorUserId,
                    auditWrite.ActorDisplayName,
                    TraceId = traceId,
                    auditWrite.DiffSummaryJson,
                    OccurredAtUtc = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            // INSERT 影响行数异常说明写入未按预期落库；必须抛出以触发外层事务回滚，
            // 而不是吞掉异常导致业务写入提交但审计静默丢失。
            throw new InvalidOperationException(
                $"Tenancy domain audit insert affected {affectedRows} rows instead of 1.");
        }
    }
}
