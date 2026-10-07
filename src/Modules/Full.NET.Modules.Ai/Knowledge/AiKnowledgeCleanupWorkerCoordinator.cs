using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>恢复墓碑及显式开启的旧代次清理，无需模型派发权限；每项在可信范围重验所有权。</summary>
internal sealed class AiKnowledgeCleanupWorkerCoordinator(IQueryExecutor query, IDataTransactionState transactions,
    IServiceScopeFactory scopes, IClock clock, IOptions<DatabaseOptions> database, IOptions<AiKnowledgeCleanupWorkerOptions> options,
    AiKnowledgeCleanupCursor cursor, ILogger<AiKnowledgeCleanupWorkerCoordinator> logger)
{
    internal async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        Guard(token);
        var configuration = options.Value;
        if (configuration.BatchSize is < 1 or > 32 || configuration.PollMilliseconds is < 250 or > 60000
            || configuration.RetiredGenerationRetentionHours is < 24 or > 8760)
            throw new InvalidOperationException("Knowledge cleanup configuration is outside its bounds.");
        if (!configuration.Enabled) return 0;
        await cursor.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Guard(token);
            var now = clock.UtcNow;
            var retiredBefore = configuration.RetiredGenerationCleanupEnabled ? now.AddHours(-configuration.RetiredGenerationRetentionHours) : now;
            var sql = database.Value.Provider switch
            {
                DatabaseProvider.SqlServer => AiKnowledgeCleanupWorkerSql.ScanSqlServer,
                DatabaseProvider.MySql => AiKnowledgeCleanupWorkerSql.ScanMySql,
                _ => throw new InvalidOperationException("Unsupported knowledge cleanup provider."),
            };
            var rows = await query.QueryAsync<AiKnowledgeCleanupRecord>(sql, new Dictionary<string, object?>
            {
                ["BatchSize"] = configuration.BatchSize, ["Now"] = now, ["HasCursor"] = cursor.Id.HasValue ? 1 : 0,
                ["RetiredEnabled"] = configuration.RetiredGenerationCleanupEnabled ? 1 : 0, ["RetiredBefore"] = retiredBefore,
                ["AfterCreatedAtUtc"] = cursor.CreatedAtUtc, ["AfterKind"] = cursor.Kind, ["AfterId"] = cursor.Id,
            }, token).ConfigureAwait(false);
            Guard(token);
            if (rows.Count > configuration.BatchSize) throw new InvalidOperationException("Knowledge cleanup scan exceeded its bound.");
            foreach (var row in rows)
            {
                Guard(token);
                try
                {
                    if (row.Id == Guid.Empty || row.Kind is < 0 or > 2 || (row.Kind == 2 && !configuration.RetiredGenerationCleanupEnabled) || row.KnowledgeBaseId == Guid.Empty
                        || row.DocumentId == Guid.Empty || row.OwnerUserId == Guid.Empty || row.TenantId == Guid.Empty) continue;
                    // 每项独立恢复范围，不捕获父请求的身份或 DbSession；超时后让下一项得到处理机会。
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(30));
                    await using var scope = scopes.CreateAsyncScope();
                    var services = scope.ServiceProvider;
                    var context = services.GetRequiredService<ICurrentTenantContextWriter>();
                    if (row.TenantId is { } tenantId) context.SetTenant(new(tenantId, tenantId.ToString("N"), tenantId.ToString("N")));
                    else context.SetHost();
                    Guard(deadline.Token, services.GetRequiredService<IDataTransactionState>());
                    if (row.Kind == 0)
                        await services.GetRequiredService<AiKnowledgeIndexCleanupStore>()
                            .CleanupTaskAsync(row.KnowledgeBaseId, row.DocumentId, row.OwnerUserId, row.Id, deadline.Token).ConfigureAwait(false);
                    else if (row.Kind == 1)
                        _ = await services.GetRequiredService<AiKnowledgeImportCoordinator>()
                            .RetryAsync(row.KnowledgeBaseId, row.DocumentId, row.Id, row.OwnerUserId, deadline.Token).ConfigureAwait(false);
                    else
                        await services.GetRequiredService<AiKnowledgeIndexCleanupStore>()
                            .CleanupRetiredTaskAsync(row.KnowledgeBaseId, row.DocumentId, row.OwnerUserId, row.Id, retiredBefore, deadline.Token).ConfigureAwait(false);
                    Guard(deadline.Token, services.GetRequiredService<IDataTransactionState>());
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    Guard(token);
                    // 不记录外部异常消息、正文、SQL 或凭据；失败记录保持持久意图，回绕后再试。
                    logger.LogWarning("Knowledge cleanup {WorkId} failed; kind {WorkKind}, type {FailureType}.", row.Id, row.Kind, exception.GetType().Name);
                }
                finally
                {
                    cursor.CreatedAtUtc = row.CreatedAtUtc; cursor.Kind = row.Kind; cursor.Id = row.Id;
                }
            }
            if (rows.Count < configuration.BatchSize) cursor.Reset();
            return rows.Count;
        }
        finally { cursor.Gate.Release(); }
    }

    private void Guard(CancellationToken token, IDataTransactionState? child = null)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction || child?.HasTransaction == true)
            throw new InvalidOperationException("Knowledge cleanup worker cannot run inside a transaction.");
    }
}
