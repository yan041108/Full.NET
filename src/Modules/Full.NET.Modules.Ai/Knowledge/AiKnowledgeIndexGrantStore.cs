using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>原会话收费委托的事务围栏与阶段门禁；新登录不能接管，未知费用不得返还或重发。</summary>
internal sealed class AiKnowledgeIndexGrantStore(IQueryExecutor query, ICommandExecutor commands, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids,
    IBackgroundSessionAuthorization authorization, IOptions<AiKnowledgeIndexGrantOptions> options)
{
    internal bool Enabled => options.Value.Enabled;
    internal bool IsTenant => AiKnowledgeScope.IsTenant(tenant);

    internal async Task<bool> CanEnqueueAsync(Guid grantId, AiKnowledgeIndexTaskBinding binding, SessionBindingSnapshot original, CancellationToken token)
    {
        Outside(token); var row = await LoadAsync(grantId, token).ConfigureAwait(false); Outside(token);
        return row is not null && Enabled && Live(row) && row.OwnerUserId == binding.OwnerUserId
            && row.KnowledgeBaseId == binding.KnowledgeBaseId && row.KnowledgeBaseVersion == binding.KnowledgeBaseVersion
            && AiKnowledgeIndexGrantPolicy.Session(row) == original && await AuthorizedAsync(row, token).ConfigureAwait(false);
    }

    /// <summary>最终派发查询前再复核独立委托权限；随后由同一权威来源 SQL 校验当前撤销和期限。</summary>
    internal async Task<bool> AuthorizeTaskDispatchAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, CancellationToken token)
    {
        Outside(token); var scopeTenant = tenant.Id; var scoped = IsTenant;
        var row = await TaskAsync(binding, taskId, token).ConfigureAwait(false); Outside(token);
        if (scopeTenant != tenant.Id || scoped != IsTenant) return false;
        return row is null || (Enabled && Live(row) && await AuthorizedAsync(row, token).ConfigureAwait(false));
    }

    /// <summary>仅由任务与原会话创建的同一事务调用；任何第二笔失败必须使整个任务回滚。</summary>
    internal async Task<bool> AttachCoreAsync(Guid grantId, Guid taskId, AiKnowledgeIndexTaskBinding binding,
        SessionBindingSnapshot original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!transactions.HasTransaction || !Enabled || original.TenantId != tenant.Id) return false;
        var row = await LoadAsync(grantId, token).ConfigureAwait(false);
        if (row is null || !Live(row) || row.AdmittedDocuments >= row.DocumentLimit || row.OwnerUserId != binding.OwnerUserId
            || row.KnowledgeBaseId != binding.KnowledgeBaseId || row.KnowledgeBaseVersion != binding.KnowledgeBaseVersion
            || AiKnowledgeIndexGrantPolicy.Session(row) != original) return false;
        var p = Parameters(row); p["GrantVersion"] = row.Version; p["TaskId"] = taskId; p["GrantItemId"] = ids.NewId();
        if (await commands.ExecuteAsync(IsTenant ? AiKnowledgeIndexGrantSql.ConsumeTenant : AiKnowledgeIndexGrantSql.ConsumeHost, p, token).ConfigureAwait(false) != 1)
            return false;
        if (await commands.ExecuteAsync(IsTenant ? AiKnowledgeIndexGrantSql.AttachTenant : AiKnowledgeIndexGrantSql.AttachHost, p, token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Knowledge index grant task binding was not created.");
        token.ThrowIfCancellationRequested();
        return Live(row) && original.TenantId == tenant.Id;
    }

    /// <summary>手工任务不附带增量授权；被授权的任务必须保留原会话且每阶段仍获独立收费委托权限。</summary>
    internal async Task<bool> CanExecuteTaskAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId,
        SessionBindingSnapshot original, CancellationToken token)
    {
        Outside(token);
        var originalTenant = tenant.Id; var originalIsTenant = IsTenant;
        var row = await TaskAsync(binding, taskId, token).ConfigureAwait(false);
        Outside(token);
        if (tenant.Id != originalTenant || IsTenant != originalIsTenant || original.TenantId != originalTenant) return false;
        if (row is null) return true;
        if (!Enabled || !Live(row) || row.TaskId != taskId || AiKnowledgeIndexGrantPolicy.Session(row) != original) return false;
        if (!await AuthorizedAsync(row, token).ConfigureAwait(false)) return false;
        var current = await TaskAsync(binding, taskId, token).ConfigureAwait(false);
        Outside(token);
        // 同授权的其他文档准入会增加计数和修订；它们不撤销本任务，比较不可变身份及实时撤销状态即可。
        return current is not null && Immutable(current) == Immutable(row) && Live(current) && original.TenantId == tenant.Id;
    }

    /// <summary>批次身份已经持久化后再消耗委托额度；顺序围栏保证重复批次不会重复消耗，旧批次也不能换身份。</summary>
    internal async Task<bool> ReserveBatchAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, int ordinal, long inputTokens, CancellationToken token)
    {
        Outside(token);
        if (ordinal is < 0 or > 4095 || inputTokens < 0) return false;
        var originalTenant = tenant.Id; var originalIsTenant = IsTenant;
        var row = await TaskAsync(binding, taskId, token).ConfigureAwait(false);
        Outside(token);
        if (tenant.Id != originalTenant || IsTenant != originalIsTenant) return false;
        if (row is null) return true;
        if (!Enabled || !Live(row) || !await AuthorizedAsync(row, token).ConfigureAwait(false)) return false;
        // PrepareAsync 已将任务、批次序号及完整请求指纹唯一绑定；不接受调用方自造的旧序号身份。
        var p = Parameters(row); p["TaskId"] = taskId; p["BatchOrdinal"] = ordinal; p["PreviousOrdinal"] = ordinal - 1;
        p["InputTokens"] = inputTokens; p["InputTokenLimitPerDocument"] = row.InputTokenLimitPerDocument;
        return await transaction.ExecuteAsync(async ct =>
        {
            var current = await TaskAsync(binding, taskId, ct).ConfigureAwait(false);
            if (current is null || !Live(current) || current.Id != row.Id || current.TenantId != tenant.Id) return false;
            if (ordinal <= current.LastBatchOrdinal) return true;
            if (ordinal != current.LastBatchOrdinal + 1 || inputTokens > current.InputTokenLimitPerDocument - current.ReservedInputTokens) return false;
            var affected = await commands.ExecuteAsync(IsTenant ? AiKnowledgeIndexGrantSql.ChargeTenant : AiKnowledgeIndexGrantSql.ChargeHost, p, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (affected == 1 && (!Live(current) || current.TenantId != tenant.Id))
                throw new InvalidOperationException("Knowledge index grant changed during admission.");
            return affected == 1;
        }, token).ConfigureAwait(false);
    }

    internal Task<AiKnowledgeIndexGrantRecord?> LoadAsync(Guid id, CancellationToken token) => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexGrantRecord>(
        IsTenant ? AiKnowledgeIndexGrantSql.LoadTenant : AiKnowledgeIndexGrantSql.LoadHost, new Dictionary<string, object?> { ["GrantId"] = id }, token);

    internal async Task<bool> AuthorizedAsync(AiKnowledgeIndexGrantRecord row, CancellationToken token)
    {
        Outside(token); var original = AiKnowledgeIndexGrantPolicy.Session(row);
        if (original is null || original.TenantId != tenant.Id || !Live(row)) return false;
        var actor = await authorization.AuthorizeAsync(original, AiKnowledgeIndexGrantPermissions.Create, token).ConfigureAwait(false);
        Outside(token);
        return actor is not null && actor.UserId == original.UserId && actor.TenantId == original.TenantId
            && actor.SessionId == original.SessionId && original.TenantId == tenant.Id && Live(row);
    }

    internal Dictionary<string, object?> Parameters(AiKnowledgeIndexGrantRecord row) => new()
    { ["GrantId"] = row.Id, ["KnowledgeBaseId"] = row.KnowledgeBaseId, ["KnowledgeBaseVersion"] = row.KnowledgeBaseVersion,
        ["OwnerUserId"] = row.OwnerUserId, ["Now"] = clock.UtcNow };
    private Task<AiKnowledgeIndexGrantRecord?> TaskAsync(AiKnowledgeIndexTaskBinding binding, Guid id, CancellationToken token) => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexGrantRecord>(
        IsTenant ? AiKnowledgeIndexGrantSql.TaskTenant : AiKnowledgeIndexGrantSql.TaskHost,
        new Dictionary<string, object?> { ["TaskId"] = id, ["KnowledgeBaseId"] = binding.KnowledgeBaseId, ["OwnerUserId"] = binding.OwnerUserId }, token);
    private bool Live(AiKnowledgeIndexGrantRecord row) => row.TenantId == tenant.Id && AiKnowledgeIndexGrantPolicy.Live(row, clock.UtcNow);
    private static AiKnowledgeIndexGrantRecord Immutable(AiKnowledgeIndexGrantRecord row) => row with
    { Version = 0, AdmittedDocuments = 0, LastBatchOrdinal = -1, ReservedInputTokens = 0 };
    private void Outside(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge index grant authority cannot join an ambient transaction.");
    }
}
