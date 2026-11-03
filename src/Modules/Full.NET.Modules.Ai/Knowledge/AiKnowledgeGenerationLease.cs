using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>读取租约仅保护资产不被回收，不能替代当前来源授权；到期取消且不续租。</summary>
internal sealed class AiKnowledgeGenerationLease : IAsyncDisposable
{
    private readonly ICommandExecutor commands;
    private readonly IQueryExecutor queries;
    private readonly ICommandTransaction transaction;
    private readonly DatabaseProvider provider;
    private readonly IClock clock;
    private readonly Dictionary<string, object?> parameters;
    private readonly bool scoped;
    private readonly CancellationTokenSource deadline;
    private readonly DateTimeOffset expires;
    private AiKnowledgeGenerationLease(ICommandExecutor commands, IQueryExecutor queries, ICommandTransaction transaction,
        DatabaseProvider provider, IClock clock, Dictionary<string, object?> parameters, bool scoped, DateTimeOffset expires, CancellationTokenSource deadline)
    {
        this.commands = commands; this.queries = queries; this.transaction = transaction; this.provider = provider;
        this.clock = clock; this.parameters = parameters; this.scoped = scoped; this.expires = expires; this.deadline = deadline;
    }
    internal CancellationToken Token => deadline.Token;
    internal bool IsValid => !Token.IsCancellationRequested && clock.UtcNow < expires;
    internal static async Task<AiKnowledgeGenerationLease?> AcquireAsync(AiKnowledgeIndexStatusRecord source, Guid actor,
        IQueryExecutor queries, ICommandExecutor commands, ICommandTransaction transaction, ICurrentTenant tenant,
        IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database, CancellationToken token)
    {
        if (actor == Guid.Empty || source.TaskId is not { } task || source.IsDeleted != 0 || source.IsCurrent != 1) return null;
        var scoped = AiKnowledgeScope.IsTenant(tenant); var tenantId = tenant.Id;
        var now = clock.UtcNow; var expires = now.AddSeconds(25);
        var parameters = new Dictionary<string, object?> { ["Id"] = ids.NewId(), ["KnowledgeBaseId"] = source.KnowledgeBaseId,
            ["DocumentId"] = source.DocumentId, ["TaskId"] = task, ["PublicationId"] = source.PublicationId,
            ["ActorUserId"] = actor, ["Now"] = now, ["ExpiresAtUtc"] = expires };
        var acquired = await transaction.ExecuteAsync(async ct =>
        {
            if (await queries.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, scoped), parameters, ct).ConfigureAwait(false) != source.DocumentId) return false;
            ct.ThrowIfCancellationRequested();
            if (clock.UtcNow >= expires || tenant.Id != tenantId || AiKnowledgeScope.IsTenant(tenant) != scoped) return false;
            _ = await commands.ExecuteAsync(scoped ? AiKnowledgePublicationSql.ExpireLeasesTenant : AiKnowledgePublicationSql.ExpireLeasesHost, parameters, ct).ConfigureAwait(false);
            return await commands.ExecuteAsync(scoped ? AiKnowledgePublicationSql.LeaseTenant : AiKnowledgePublicationSql.LeaseHost, parameters, ct).ConfigureAwait(false) == 1;
        }, token).ConfigureAwait(false);
        if (!acquired) return null;
        var actual = await queries.QuerySingleOrDefaultAsync<AiKnowledgeLeaseDeadline>(scoped ? AiKnowledgePublicationSql.LeaseDeadlineTenant : AiKnowledgePublicationSql.LeaseDeadlineHost, parameters, token).ConfigureAwait(false);
        if (actual is null) expires = now;
        else if (actual.ExpiresAtUtc < expires) expires = actual.ExpiresAtUtc;
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        var remaining = expires - clock.UtcNow;
        if (remaining > TimeSpan.Zero) deadline.CancelAfter(remaining); else deadline.Cancel();
        return new(commands, queries, transaction, database.Value.Provider, clock, parameters, scoped, expires, deadline);
    }
    public async ValueTask DisposeAsync()
    {
        await deadline.CancelAsync().ConfigureAwait(false); deadline.Dispose();
        // 取消不放弃数据库命令；释放失败由固定过期时间收敛，不能延长读资格。
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            // 释放也先锁文档根，再删租约，避免与申请/清理形成“租约→文档”的反向锁序。
            _ = await transaction.ExecuteAsync(async token =>
            {
                if (await queries.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(provider, scoped), parameters, token).ConfigureAwait(false)
                    != (Guid)parameters["DocumentId"]!) return 0;
                return await commands.ExecuteAsync(scoped ? AiKnowledgePublicationSql.ReleaseTenant : AiKnowledgePublicationSql.ReleaseHost, parameters, token).ConfigureAwait(false);
            }, cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DataCommandException or OperationCanceledException) { }
    }
}

/// <summary>读取租约在发布有效期以内截短，不提前拒绝仍有效的最后一段读取窗口。</summary>
internal sealed record AiKnowledgeLeaseDeadline(DateTimeOffset ExpiresAtUtc);
