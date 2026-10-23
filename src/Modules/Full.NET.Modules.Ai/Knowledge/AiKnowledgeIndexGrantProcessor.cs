using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Diagnostics;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>单 Worker 串行扫描原授权，每轮每授权最多一个新来源；多实例通过授权 CAS 和任务唯一键收敛。</summary>
internal sealed class AiKnowledgeIndexGrantProcessor(IServiceScopeFactory scopes, IOptions<AiKnowledgeIndexGrantOptions> options,
    ILogger<AiKnowledgeIndexGrantProcessor> logger, AiDataProcessingPolicy? dataPolicy = null) : BackgroundService
{
    private Guid? afterId;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Enabled) return;
        if (config.BatchSize is < 1 or > 16 || config.PollMilliseconds is < 1000 or > 60000)
            throw new InvalidOperationException("Knowledge grant worker configuration is outside its bounds.");
        while (!stoppingToken.IsCancellationRequested)
        {
            using var observation = AiDataObservationScope.Enter(dataPolicy ?? AiDataProcessingPolicy.Default);
            try { await ProcessAsync(config.BatchSize, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception failure) { AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.KnowledgeIncrementalRecoveryIteration, failure); }
            try { await Task.Delay(config.PollMilliseconds, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task<int> ProcessAsync(int take, CancellationToken token)
    {
        if (!options.Value.Enabled) return 0;
        if (take is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(take));
        await using var root = scopes.CreateAsyncScope(); var provider = root.ServiceProvider;
        var context = provider.GetRequiredService<ICurrentTenantContextWriter>();
        context.SetHost();
        try { return await ScanAsync(provider, take, token).ConfigureAwait(false); }
        finally { context.Clear(); }
    }

    private async Task<int> ScanAsync(IServiceProvider provider, int take, CancellationToken token)
    {
        var transaction = provider.GetRequiredService<IDataTransactionState>();
        if (transaction.HasTransaction) throw new InvalidOperationException("Knowledge grant scan cannot join an ambient transaction.");
        var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider;
        var sql = database switch { DatabaseProvider.SqlServer => AiKnowledgeIndexGrantSql.ScanSqlServer,
            DatabaseProvider.MySql => AiKnowledgeIndexGrantSql.ScanMySql, _ => throw new InvalidOperationException("Unsupported knowledge grant provider.") };
        var rows = await provider.GetRequiredService<IQueryExecutor>().QueryAsync<AiKnowledgeIndexGrantAnchor>(sql, new Dictionary<string, object?>
        { ["Now"] = provider.GetRequiredService<IClock>().UtcNow, ["BatchSize"] = take, ["HasCursor"] = afterId.HasValue ? 1 : 0,
            ["AfterId"] = afterId ?? new Guid("00000000-0000-0000-0000-000000000001") }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (rows.Count > take || rows.Any(row => row.Id == Guid.Empty || row.TenantId == Guid.Empty) || rows.Select(row => row.Id).Distinct().Count() != rows.Count)
            throw new InvalidDataException("Invalid knowledge grant scan.");
        foreach (var anchor in rows)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
                await using var scope = scopes.CreateAsyncScope(); var child = scope.ServiceProvider;
                var context = child.GetRequiredService<ICurrentTenantContextWriter>();
                if (anchor.TenantId is { } tenantId) context.SetTenant(new(tenantId, "ai-index-grant", "AI index grant")); else context.SetHost();
                try
                {
                    var grants = child.GetRequiredService<AiKnowledgeIndexGrantStore>();
                    var row = await grants.LoadAsync(anchor.Id, deadline.Token).ConfigureAwait(false);
                    if (row is not null && row.TenantId == anchor.TenantId && AiKnowledgeIndexGrantPolicy.Live(row, child.GetRequiredService<IClock>().UtcNow)
                        && row.AdmittedDocuments < row.DocumentLimit && await grants.AuthorizedAsync(row, deadline.Token).ConfigureAwait(false))
                    {
                        var candidates = database == DatabaseProvider.SqlServer
                            ? grants.IsTenant ? AiKnowledgeIndexGrantSql.CandidatesSqlServerTenant : AiKnowledgeIndexGrantSql.CandidatesSqlServerHost
                            : grants.IsTenant ? AiKnowledgeIndexGrantSql.CandidatesMySqlTenant : AiKnowledgeIndexGrantSql.CandidatesMySqlHost;
                        var parameters = grants.Parameters(row);
                        parameters["AllowDocuments"] = child.GetRequiredService<IOptions<AiKnowledgeDocumentRuntimeOptions>>().Value.Enabled ? 1 : 0;
                        parameters["AllowPdf"] = child.GetRequiredService<IOptions<AiKnowledgePdfRuntimeOptions>>().Value.Enabled ? 1 : 0;
                        parameters["AllowDocx"] = child.GetRequiredService<IOptions<AiKnowledgeDocxRuntimeOptions>>().Value.Enabled ? 1 : 0;
                        var source = await child.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AiKnowledgeIndexEnqueueRecord>(candidates, parameters, deadline.Token).ConfigureAwait(false);
                        var original = AiKnowledgeIndexGrantPolicy.Session(row);
                        if (source is not null && original is not null)
                            _ = await child.GetRequiredService<AiKnowledgeIndexEnqueueService>().EnqueueAsync(row.KnowledgeBaseId, source.DocumentId,
                                source.VersionId, original, deadline.Token, row.Id).ConfigureAwait(false);
                    }
                }
                finally { context.Clear(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception failure) { AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.KnowledgeIncrementalRecovery, failure, anchor.Id); }
            // 单项失败不阻塞其余授权；绕回后仍不重试已存在或未知收费任务。
            afterId = anchor.Id;
        }
        if (rows.Count < take) afterId = null;
        return rows.Count;
    }
}
