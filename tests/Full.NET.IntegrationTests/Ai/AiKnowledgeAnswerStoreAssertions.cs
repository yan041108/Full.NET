using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Abstractions.Messaging;
using Full.NET.IntegrationTests.Migrations;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>现有真实 TXT/PDF 双库场景同时验证生产落库边界与消息锁等待跨越租约后的回滚。</summary>
internal static class AiKnowledgeAnswerStoreAssertions
{
    internal static async Task<(Guid SessionId, Guid MessageId)> VerifyAsync(DbConnection db, IQueryExecutor query, ICommandExecutor commands,
        ICommandTransaction transaction, ICurrentTenant tenant, IClock clock, IOptions<DatabaseOptions> database,
        AiKnowledgeAnswerHistoryGuard guard, AiKnowledgePreparedAnswer prepared, AiKnowledgeAnswer accepted)
    {
        // 账本场景的测试时钟可被推进；租约等待场景必须与数据库实际 UTC 同步，不能改动原预算时钟。
        var liveClock = new LiveClock();
        (Guid SessionId, Guid MessageId) completed = default;
        foreach (var waitPastExpiry in new[] { false, true })
        {
            var messageId = Guid.CreateVersion7(); var sessionId = Guid.CreateVersion7(); var actor = prepared.Retrieval.Actor;
            var now = liveClock.UtcNow; var expiry = now.AddSeconds(waitPastExpiry ? 4 : 60);
            await db.ExecuteAsync("INSERT INTO fn_ai_chat_session (Id,TenantId,OwnerUserId,ModelConfigId,ModelName,Title,IsGenerating,GenerationId,GenerationExpiresAtUtc,GenerationCancellationRequested,CreatedAtUtc) VALUES (@SessionId,@TenantId,@Actor,@ModelConfigId,'model','答案落库',1,@MessageId,@Expiry,0,@Now)",
                new { SessionId = sessionId, MessageId = messageId, TenantId = tenant.Id, Actor = actor, ModelConfigId = prepared.Retrieval.Model.Id, Expiry = expiry, Now = now });
            var parameters = new Dictionary<string, object?> { ["SessionId"] = sessionId, ["MessageId"] = messageId, ["OwnerUserId"] = actor, ["Now"] = now };
            Assert.AreEqual(1, await commands.ExecuteAsync(AiKnowledgeAnswerSql.InsertFor(tenant.Id.HasValue), parameters));
            var executed = new AiKnowledgeExecutedAnswer(prepared, accepted, Guid.CreateVersion7(), Guid.CreateVersion7());
            if (!waitPastExpiry)
            {
                var store = new AiKnowledgeAnswerStore(commands, query, transaction, tenant, liveClock, database, guard);
                Assert.IsTrue(await store.CompleteAsync(executed, messageId, sessionId, actor, default));
                Assert.AreEqual(accepted.Text, await db.QuerySingleAsync<string>("SELECT Content FROM fn_ai_chat_message WHERE Id=@MessageId", parameters));
                var json = await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@MessageId", parameters);
                var lineage = AiKnowledgeAnswerLineageCodec.Decode(json, messageId, sessionId, actor, tenant.Id);
                Assert.AreEqual(prepared.Retrieval.Sources.Count, lineage.Dependencies.Count);
                Assert.IsFalse(await store.CompleteAsync(executed, messageId, sessionId, actor, default));
                completed = (sessionId, messageId);
                var corruptIds = new List<Guid>();
                foreach (var kind in new[] { "missing", "corrupt" })
                {
                    var id = Guid.CreateVersion7(); corruptIds.Add(id);
                    await db.ExecuteAsync("INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc,IsKnowledgeAnswer,KnowledgeLineage) VALUES (@Id,@SessionId,'assistant',@Content,'completed',@Now,@Marked,@Lineage)",
                        new { Id = id, SessionId = sessionId, Content = "损坏派生正文", Now = now, Marked = true, Lineage = kind == "missing" ? null : "{}" });
                }
                // 不降低正式约束来制造坏数据；未标记却带依赖的组合应由两库直接拒绝。
                await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc,IsKnowledgeAnswer,KnowledgeLineage) VALUES (@Id,@SessionId,'assistant',@Content,'completed',@Now,0,'{}')",
                    new { Id = Guid.CreateVersion7(), SessionId = sessionId, Content = "禁止插入的不一致正文", Now = now }));
                var ordinaryId = Guid.CreateVersion7();
                await db.ExecuteAsync("INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc) VALUES (@Id,@SessionId,'user',@Content,'completed',@Now)",
                    new { Id = ordinaryId, SessionId = sessionId, Content = "普通上下文", Now = now });
                var history = await new AiChatSessionQueryService(query, tenant, database, liveClock, guard).GetByIdAsync(sessionId, actor);
                Assert.IsTrue(history.IsSuccess);
                var answer = history.Value!.Messages.Single(message => message.Id == messageId);
                Assert.AreEqual(accepted.Text, answer.Content); Assert.IsNull(answer.KnowledgeUnavailableCode);
                Assert.AreEqual(accepted.Citations.Count, answer.Citations.Count);
                foreach (var id in corruptIds)
                { var hidden = history.Value.Messages.Single(message => message.Id == id); Assert.AreEqual("", hidden.Content);
                    Assert.AreEqual(AiErrorCodes.KnowledgeAnswerUnavailable, hidden.KnowledgeUnavailableCode); Assert.HasCount(0, hidden.Citations); }
                var modelHistory = await query.QueryAsync<AiChatMessageRecord>(database.Value.Provider == DatabaseProvider.MySql
                    ? new SqlStatement("ai.list_recent_chat_messages_for_model.mysql", AiChatSql.ListRecentMessagesForModelMySql, SqlDataScope.Global)
                    : AiChatSql.ListRecentMessagesForModel, new Dictionary<string, object?> { ["SessionId"] = sessionId, ["Take"] = 24 });
                Assert.HasCount(1, modelHistory); Assert.AreEqual(ordinaryId, modelHistory[0].Id);
                continue;
            }
            await using var blocking = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(database.Value.Provider == DatabaseProvider.SqlServer, database.Value.ConnectionString);
            await blocking.OpenAsync(); await using var held = await blocking.BeginTransactionAsync();
            await blocking.ExecuteAsync("UPDATE fn_ai_chat_message SET Content='' WHERE Id=@MessageId", parameters, held);
            var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting = new AiKnowledgeAnswerStore(commands, new NotifyingQuery(query, locked), transaction, tenant, liveClock, database, guard)
                .CompleteAsync(executed, messageId, sessionId, actor, default);
            try
            {
                await locked.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.IsFalse(waiting.IsCompleted, "完成写入必须等待独立连接持有的消息锁。");
                var delay = expiry - liveClock.UtcNow + TimeSpan.FromMilliseconds(250);
                if (delay > TimeSpan.Zero) await Task.Delay(delay);
            }
            finally { await held.CommitAsync(); }
            Assert.IsFalse(await waiting.WaitAsync(TimeSpan.FromSeconds(20)), "锁等待跨过到期后不能报告成功。");
            Assert.AreEqual("", await db.QuerySingleAsync<string>("SELECT Content FROM fn_ai_chat_message WHERE Id=@MessageId", parameters));
            Assert.AreEqual("streaming", await db.QuerySingleAsync<string>("SELECT StatusKey FROM fn_ai_chat_message WHERE Id=@MessageId", parameters));
            Assert.IsNull(await db.QuerySingleOrDefaultAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@MessageId", parameters));
        }
        return completed;
    }
    private sealed class LiveClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class NotifyingQuery(IQueryExecutor inner, TaskCompletionSource locked) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => inner.QueryAsync<T>(statement, parameters, cancellationToken);
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var result = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            if (statement.Name.Contains(".lock_", StringComparison.Ordinal)) locked.TrySetResult();
            return result;
        }
    }
}
