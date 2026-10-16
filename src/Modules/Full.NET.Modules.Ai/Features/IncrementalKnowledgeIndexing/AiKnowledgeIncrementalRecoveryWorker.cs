using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Diagnostics;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>只补齐已确认来源的意图；扫描不授予用户身份、文件访问、预算或模型派发资格。</summary>
internal sealed class AiKnowledgeIncrementalRecoveryWorker(IQueryExecutor query, IDataTransactionState transactions,
    ICurrentTenant tenant, IServiceScopeFactory scopes, IOptions<DatabaseOptions> database,
    IOptions<AiKnowledgeIncrementalRecoveryWorkerOptions> options, AiKnowledgeIncrementalRecoveryCursor cursor,
    ILogger<AiKnowledgeIncrementalRecoveryWorker> logger)
{
    /// <summary>返回本轮尝试的来源数，不是创建数；失败项推进后在绕回时重试，取消保留当前位置。</summary>
    internal async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        Guard(token);
        var configuration = options.Value;
        if (configuration.BatchSize is < 1 or > 32 || configuration.PollMilliseconds is < 1000 or > 60000)
            throw new InvalidOperationException("Knowledge incremental recovery configuration is outside its bounds.");
        if (!configuration.Enabled) return 0;
        await cursor.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Guard(token);
            var sql = database.Value.Provider switch
            {
                DatabaseProvider.SqlServer => AiKnowledgeIncrementalRecoveryWorkerSql.ScanSqlServer,
                DatabaseProvider.MySql => AiKnowledgeIncrementalRecoveryWorkerSql.ScanMySql,
                _ => throw new InvalidOperationException("Unsupported knowledge incremental recovery provider.")
            };
            var rows = await query.QueryAsync<AiKnowledgeSourceConfirmed>(sql, new Dictionary<string, object?>
            {
                ["BatchSize"] = configuration.BatchSize, ["HasCursor"] = cursor.Id.HasValue ? 1 : 0,
                // 首轮关闭游标条件，不用空 UUID 作为数据库参数。
                ["AfterDocumentId"] = cursor.Id ?? new Guid("00000000-0000-0000-0000-000000000001")
            }, token).ConfigureAwait(false);
            Guard(token);
            if (rows.Count > configuration.BatchSize || rows.Any(r => r.KnowledgeBaseId == Guid.Empty || r.DocumentId == Guid.Empty || r.VersionId == Guid.Empty || r.TenantId == Guid.Empty)
                || rows.Select(r => r.DocumentId).Distinct().Count() != rows.Count)
                throw new InvalidOperationException("Knowledge incremental scan returned invalid source identities.");
            foreach (var row in rows)
            {
                Guard(token);
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(30));
                    // 全局锚点只能用于建立全新范围，不能把父 DbSession 或上一个租户带入当前文档。
                    await using var scope = scopes.CreateAsyncScope();
                    var provider = scope.ServiceProvider;
                    var context = provider.GetRequiredService<ICurrentTenantContextWriter>();
                    if (row.TenantId is { } id) context.SetTenant(new(id, "ai-source-recovery", "AI source recovery"));
                    else context.SetHost();
                    var child = provider.GetRequiredService<IDataTransactionState>();
                    Guard(deadline.Token, child);
                    // 与来源事件复用同一短事务；锁等待后重验当前范围、墓碑、启用状态和最新完成来源。
                    _ = await provider.GetRequiredService<AiKnowledgeIncrementalIndexWriter>().ApplyAsync(row, deadline.Token).ConfigureAwait(false);
                    Guard(deadline.Token, child);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception failure)
                {
                    Guard(token);
                    AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.KnowledgeIncrementalRecovery, failure, row.DocumentId);
                }
                cursor.Id = row.DocumentId;
            }
            // 包括空页；失败项、游标之前的新来源与重新启用目录会在下一次完整扫描中被重新发现。
            if (rows.Count < configuration.BatchSize) cursor.Id = null;
            return rows.Count;
        }
        finally { cursor.Gate.Release(); }
    }

    private void Guard(CancellationToken token, IDataTransactionState? child = null)
    {
        token.ThrowIfCancellationRequested();
        if (!tenant.IsHost || tenant.Id.HasValue || transactions.HasTransaction || child?.HasTransaction == true)
            throw new InvalidOperationException("Knowledge incremental scan requires a host scope without an ambient transaction.");
    }
}
