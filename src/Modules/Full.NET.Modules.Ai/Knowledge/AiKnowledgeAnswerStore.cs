using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Abstractions.Messaging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>内部答案落库边界：完整来源资格在事务外复核，正文和依赖由一笔租约条件更新原子保存。</summary>
/// <remarks>公开 Ask 先取得会话生成槽位并插入带来源标记的占位；历史消费者独立复核来源。</remarks>
internal sealed class AiKnowledgeAnswerStore(ICommandExecutor commands, IQueryExecutor query, ICommandTransaction transaction,
    ICurrentTenant tenant, IClock clock, IOptions<DatabaseOptions> database, AiKnowledgeAnswerHistoryGuard guard)
{
    /// <summary>保存最小依赖而非检索快照；资格变化或租约丢失返回 false，调用方不能交付正文。</summary>
    /// <param name="answer">收费执行器完成结算与引用校验后的内部结果。</param>
    /// <param name="messageId">当前生成代次绑定的助手消息。</param>
    /// <param name="sessionId">已授权的会话。</param>
    /// <param name="actor">可信会话所有者。</param>
    /// <param name="token">取消当前操作的令牌。</param>
    internal async Task<bool> CompleteAsync(AiKnowledgeExecutedAnswer answer, Guid messageId, Guid sessionId, Guid actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        var lineage = AiKnowledgeAnswerLineageCodec.Create(answer, messageId, sessionId);
        var json = AiKnowledgeAnswerLineageCodec.Encode(lineage);
        if (lineage.ActorUserId != actor || lineage.TenantId != tenantId || answer.Answer.Text.Length > 16000
            || await guard.AuthorizeAsync(json, messageId, sessionId, actor, true, token).ConfigureAwait(false) is null || !SameScope()) return false;
        var parameters = AiSqlParameters.Create(
            ("MessageId", messageId), ("SessionId", sessionId), ("OwnerUserId", actor),
            ("Content", answer.Answer.Text), ("KnowledgeLineage", json), ("Now", clock.UtcNow));
        try
        {
            await transaction.ExecuteAsync(async work =>
            {
                var locked = await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeAnswerSql.LockFor(database.Value.Provider, scoped), parameters, work).ConfigureAwait(false);
                if (locked != messageId || !SameScope()) throw new LostLeaseException();
                var changed = await commands.ExecuteAsync(AiKnowledgeAnswerSql.CompleteFor(scoped), parameters, work).ConfigureAwait(false);
                var current = await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeAnswerSql.CurrentFor(database.Value.Provider, scoped), parameters, work).ConfigureAwait(false);
                // 持有会话锁后，数据库时间仍可能在消息锁等待中跨越期限；抛错让正文和依赖一起回滚。
                if (changed != 1 || current != messageId || !SameScope()) throw new LostLeaseException();
                return true;
            }, token).ConfigureAwait(false);
        }
        catch (LostLeaseException) { return false; }
        if (!SameScope()) return false;
        var authorized = await guard.AuthorizeAsync(json, messageId, sessionId, actor, true, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); return authorized is not null && SameScope();
        bool SameScope() => tenant.Id == tenantId && AiKnowledgeScope.IsTenant(tenant) == scoped;
    }
    private sealed class LostLeaseException : Exception;
}
