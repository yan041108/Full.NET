using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>解析准备独立持久队列；本地事务只操作 AI 表，外部授权由入口及消费者复核。</summary>
internal sealed class AiKnowledgeCorrectionPreparationQueue(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState state, ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database)
{
    private bool Scoped => AiKnowledgeScope.IsTenant(tenant);
    internal Task<AiKnowledgeCorrectionPreparationRecord?> ReadAsync(AiKnowledgeCorrectionStore.Binding binding, Guid id, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionPreparationRecord>(Scoped ? AiKnowledgeCorrectionPreparationSql.ReadTenant : AiKnowledgeCorrectionPreparationSql.ReadHost,
            Parameters(binding, id), token);
    internal Task<AiKnowledgeCorrectionPreparationRecord?> ReadLatestAsync(AiKnowledgeCorrectionStore.Binding binding, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionPreparationRecord>(AiKnowledgeCorrectionPreparationSql.LatestFor(database.Value.Provider, Scoped), Parameters(binding), token);

    /// <summary>同一原请求精确重放；根锁下每个文件版本只有一份活动准备，不抢占另一会话。</summary>
    internal async Task<Result<AiKnowledgeCorrectionPreparationRecord>> EnqueueAsync(AiKnowledgeCorrectionContext context,
        SessionBindingSnapshot actor, Guid requestKey, CancellationToken token)
    {
        Check(token); var b = context.Binding; var scoped = Scoped; var tenantId = tenant.Id;
        if (requestKey == Guid.Empty || actor.UserId != b.OwnerUserId || actor.TenantId != tenantId) return Conflict();
        var hash = AiKnowledgeCorrectionPreparationService.RequestHash(context, actor, "queue:knowledge-correction-v1", token);
        var p = Parameters(b, ids.NewId()); p["RequestKey"] = requestKey;
        p["RequestHash"] = hash; p["SessionId"] = actor.SessionId; p["SessionKind"] = actor.SessionKind;
        p["SecurityStamp"] = actor.SecurityStamp; p["ActorScope"] = actor.ActorScope; p["EffectiveScope"] = actor.EffectiveScope;
        p["Now"] = Now();
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(b, p, scoped, tenantId, ct).ConfigureAwait(false)) return Conflict();
            var existing = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionPreparationRecord>(scoped ? AiKnowledgeCorrectionPreparationSql.KeyTenant : AiKnowledgeCorrectionPreparationSql.KeyHost, p, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                ct.ThrowIfCancellationRequested();
                return Same(scoped, tenantId) && existing.RequestHash == hash && existing.ActorUserId == actor.UserId
                    ? Result<AiKnowledgeCorrectionPreparationRecord>.Success(existing) : Conflict();
            }
            var rows = await query.QueryAsync<AiKnowledgeCorrectionPreparationRecord>(AiKnowledgeCorrectionPreparationSql.ActiveFor(database.Value.Provider, scoped), p, ct).ConfigureAwait(false);
            if (rows.Count > 32) return Conflict();
            foreach (var row in rows)
            {
                if (row.IsCurrent == 1) return Conflict();
                var cancel = Parameters(b, row.Id); cancel["Now"] = p["Now"]; cancel["StateKey"] = "cancelled";
                cancel["FailureCode"] = "ai.knowledge.correction_preparation_stale"; cancel["Stale"] = 1;
                if (await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionPreparationSql.StopTenant : AiKnowledgeCorrectionPreparationSql.StopHost, cancel, ct).ConfigureAwait(false) != 1) return Conflict();
            }
            if (!Same(scoped, tenantId) || await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionPreparationSql.InsertTenant : AiKnowledgeCorrectionPreparationSql.InsertHost, p, ct).ConfigureAwait(false) != 1) return Conflict();
            var saved = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionPreparationRecord>(scoped ? AiKnowledgeCorrectionPreparationSql.ReadTenant : AiKnowledgeCorrectionPreparationSql.ReadHost, p, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return saved is not null && Same(scoped, tenantId) ? Result<AiKnowledgeCorrectionPreparationRecord>.Success(saved) : Conflict();
        }, token).ConfigureAwait(false);
    }

    /// <summary>重启最多恢复三次；解析运行时在首次领取时冻结，漂移不得换实现完成旧请求。</summary>
    internal async Task<AiKnowledgeCorrectionPreparationRecord?> AcquireAsync(AiKnowledgeCorrectionStore.Binding binding, Guid id, string? runtime, CancellationToken token)
    {
        Check(token); var scoped = Scoped; var tenantId = tenant.Id; var p = Parameters(binding, id);
        p["Now"] = Now(); p["LeaseToken"] = ids.NewId(); p["LeaseExpiresAtUtc"] = Now().AddSeconds(180); p["ParserRuntimeId"] = runtime ?? "unavailable";
        var result = await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(binding, p, scoped, tenantId, ct).ConfigureAwait(false)) return Conflict();
            var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionPreparationRecord>(scoped ? AiKnowledgeCorrectionPreparationSql.ReadTenant : AiKnowledgeCorrectionPreparationSql.ReadHost, p, ct).ConfigureAwait(false);
            if (row is null || row.StateKey is not ("queued" or "running") || row.StateKey == "running" && row.LeaseExpiresAtUtc > Now()) return Conflict();
            if (row.IsCurrent != 1 || row.AttemptCount >= 3 || runtime is null || row.ParserRuntimeId is { } frozen && frozen != runtime)
            {
                p["StateKey"] = row.IsCurrent != 1 ? "cancelled" : "failed";
                p["FailureCode"] = row.IsCurrent != 1 ? "ai.knowledge.correction_preparation_stale" : "ai.knowledge.correction_preparation_unavailable";
                p["Stale"] = row.IsCurrent != 1 ? 1 : 0;
                if (await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionPreparationSql.StopTenant : AiKnowledgeCorrectionPreparationSql.StopHost, p, ct).ConfigureAwait(false) != 1) return Conflict();
                var stopped = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionPreparationRecord>(scoped ? AiKnowledgeCorrectionPreparationSql.ReadTenant : AiKnowledgeCorrectionPreparationSql.ReadHost, p, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return stopped is not null && Same(scoped, tenantId) ? Result<AiKnowledgeCorrectionPreparationRecord>.Success(stopped) : Conflict();
            }
            if (await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionPreparationSql.ClaimTenant : AiKnowledgeCorrectionPreparationSql.ClaimHost, p, ct).ConfigureAwait(false) != 1) return Conflict();
            var held = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionPreparationRecord>(scoped ? AiKnowledgeCorrectionPreparationSql.ReadTenant : AiKnowledgeCorrectionPreparationSql.ReadHost, p, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return held is not null && Same(scoped, tenantId) ? Result<AiKnowledgeCorrectionPreparationRecord>.Success(held) : Conflict();
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value!.LeaseToken is not null && result.Value.StateKey == "running" ? result.Value : null;
    }

    /// <summary>失败只能结束本次仍持有的租约；迟到消费者不能覆盖后续重启结果。</summary>
    internal async Task FailAsync(AiKnowledgeCorrectionStore.Binding binding, Guid id, Guid lease, CancellationToken token)
    {
        Check(token); var scoped = Scoped; var tenantId = tenant.Id; var p = Parameters(binding, id);
        p["Now"] = Now(); p["LeaseToken"] = lease; p["StateKey"] = "failed"; p["FailureCode"] = "ai.knowledge.correction_preparation_unavailable";
        await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(binding, p, scoped, tenantId, ct).ConfigureAwait(false)) return Result<bool>.Success(false);
            var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionPreparationRecord>(scoped ? AiKnowledgeCorrectionPreparationSql.ReadTenant : AiKnowledgeCorrectionPreparationSql.ReadHost, p, ct).ConfigureAwait(false);
            if (row?.StateKey != "running" || row.LeaseToken != lease || row.LeaseExpiresAtUtc <= Now()) return Result<bool>.Success(false);
            var changed = await command.ExecuteAsync(scoped ? AiKnowledgeCorrectionPreparationSql.FailTenant : AiKnowledgeCorrectionPreparationSql.FailHost, p, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); return Same(scoped, tenantId) ? Result<bool>.Success(changed == 1) : Result<bool>.Failure(new Error("ai.knowledge.correction_conflict", "The scope changed.", ErrorType.Conflict));
        }, token).ConfigureAwait(false);
    }

    private async Task<bool> Lock(AiKnowledgeCorrectionStore.Binding b, Dictionary<string, object?> p, bool scoped, Guid? tenantId, CancellationToken token) =>
        Same(scoped, tenantId) && await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false) == b.DocumentId && Same(scoped, tenantId);
    private bool Same(bool scoped, Guid? id) => Scoped == scoped && tenant.Id == id && (scoped ? tenant.IsAvailable : tenant.IsHost);
    private void Check(CancellationToken token) { token.ThrowIfCancellationRequested(); if (state.HasTransaction) throw new InvalidOperationException("Preparation queue requires an independent transaction."); }
    private static Dictionary<string, object?> Parameters(AiKnowledgeCorrectionStore.Binding b, Guid? id = null)
    {
        var values = new Dictionary<string, object?> { ["KnowledgeBaseId"] = b.KnowledgeBaseId, ["DocumentId"] = b.DocumentId, ["VersionId"] = b.VersionId,
            ["OwnerUserId"] = b.OwnerUserId, ["DocumentVersion"] = b.DocumentVersion };
        if (id is { } value) { if (value == Guid.Empty) throw new ArgumentException("A preparation identity must be assigned."); values["PreparationId"] = value; }
        return values;
    }
    private DateTimeOffset Now() { var now = clock.UtcNow.ToUniversalTime(); return now.AddTicks(-(now.Ticks % 10)); }
    private static Result<AiKnowledgeCorrectionPreparationRecord> Conflict() => Result<AiKnowledgeCorrectionPreparationRecord>.Failure(new Error("ai.knowledge.correction_conflict", "The correction preparation changed or is unavailable.", ErrorType.Conflict));
}
