using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>业务历史失效不能进入普通聊天收费；不把派生答案偷偷带入未绑定来源的普通模型输入。</summary>
internal sealed class AiPersonalScheduleContinuationGate(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IOptions<DatabaseOptions> database, AiKnowledgeAnswerHistoryGuard history)
{
    internal async Task<IReadOnlyList<AiKnowledgeAnswerHistoryEntry>> CaptureAsync(Guid session, Guid actor, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        return await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(work => CaptureCoreAsync(session, actor, work), deadline.Token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AiKnowledgeAnswerHistoryEntry>> CaptureCoreAsync(Guid session, Guid actor, CancellationToken token)
    {
        var scope = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant); Check();
        if (session == Guid.Empty || actor == Guid.Empty) throw new AiPersonalScheduleContinuationUnavailableException();
        var rows = await query.QueryAsync<AiChatMessageRecord>(AiPersonalScheduleContinuationSql.For(database.Value.Provider, scoped),
            AiSqlParameters.Create(("SessionId", session), ("OwnerUserId", actor)), token).ConfigureAwait(false);
        Check(); if (rows.Count > AiKnowledgeAnswerHistoryGuard.MaxHistoryAnswers || rows.Select(value => value.Id).Distinct().Count() != rows.Count)
            throw new AiPersonalScheduleContinuationUnavailableException();
        var entries = new List<AiKnowledgeAnswerHistoryEntry>();
        foreach (var row in rows)
        {
            if (row.SessionId != session || row.RoleKey != "assistant" || row.StatusKey != "completed" || !row.IsKnowledgeAnswer)
                throw new AiPersonalScheduleContinuationUnavailableException();
            AiKnowledgeAnswerLineage lineage;
            try { lineage = AiKnowledgeAnswerLineageCodec.Decode(row.KnowledgeLineage, row.Id, session, actor, scope); }
            catch (InvalidDataException) { throw new AiPersonalScheduleContinuationUnavailableException(); }
            if (lineage.PersonalSchedules is { Count: > 0 }) entries.Add(new(row.Id, session, row.KnowledgeLineage));
        }
        if (entries.Count > 0)
        {
            var approved = await history.AuthorizeModelBatchAsync(entries, actor, token).ConfigureAwait(false); Check();
            if (approved.Count != entries.Count || entries.Any(value => !approved.ContainsKey(value.MessageId)))
                throw new AiPersonalScheduleContinuationUnavailableException();
        }
        return entries.AsReadOnly();
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (transactions.HasTransaction || tenant.Id != scope || AiKnowledgeScope.IsTenant(tenant) != scoped)
                throw new AiPersonalScheduleContinuationUnavailableException();
        }
    }

    internal async Task RevalidateAsync(IReadOnlyList<AiKnowledgeAnswerHistoryEntry> expected, Guid session, Guid actor, CancellationToken token)
    {
        if (expected.Count == 0) return;
        var current = await CaptureAsync(session, actor, token).ConfigureAwait(false);
        if (!expected.SequenceEqual(current)) throw new AiPersonalScheduleContinuationUnavailableException();
    }
}

/// <summary>固定安全失败，不向客户端暴露任何私人来源或原异常。</summary>
internal sealed class AiPersonalScheduleContinuationUnavailableException() : Exception("当前业务来源不可用于继续对话。");
