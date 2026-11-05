using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.ImportTasks;

/// <summary>来源释放意图先持久化，再调用 Files 的幂等释放；失败保留原状态以便后续迭代接续。</summary>
internal sealed class ImportSourceRetentionProcessor(IQueryExecutor queries, ICommandExecutor commands,
    ITenantResourceFileStore files, IActiveTenantContextResolver tenants, ICurrentTenantContextWriter current,
    IClock clock, IOptions<DatabaseOptions> database, IOptionsMonitor<ImportExportOptions> options,
    ImportSourceRetentionScanState cursor, ILogger<ImportSourceRetentionProcessor> logger)
{
    internal async Task<int> ProcessDueAsync(CancellationToken token)
    {
        var take = Math.Clamp(options.CurrentValue.SourceCleanupBatchSize, 1, 100);
        var after = cursor.Read();
        var statement = database.Value.Provider == DatabaseProvider.SqlServer
            ? ImportSourceRetentionSql.ListDueSqlServer : ImportSourceRetentionSql.ListDueMySql;
        Task<IReadOnlyList<ImportSourceRetentionCandidate>> ReadAsync(ImportSourceRetentionPosition? position) =>
            queries.QueryAsync<ImportSourceRetentionCandidate>(statement, ImportExportSqlParameters.Create(
                ("Take", take), ("Now", clock.UtcNow), ("AfterExpiresAtUtc", position?.ExpiresAtUtc), ("AfterId", position?.Id)), token);
        var rows = await ReadAsync(after).ConfigureAwait(false);
        if (rows.Count == 0 && after.HasValue)
        {
            cursor.Advance(null);
            rows = await ReadAsync(null).ConfigureAwait(false);
        }
        var completed = 0;
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var tenant = await tenants.ResolveActiveByIdAsync(row.TenantId, token).ConfigureAwait(false);
                if (tenant is null) continue;
                current.SetTenant(tenant);
                var task = await queries.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById,
                    ImportExportSqlParameters.Create(("Id", row.Id)), token).ConfigureAwait(false);
                var now = clock.UtcNow;
                if (task is null || task.TenantId != row.TenantId || task.SourceReleasedAtUtc.HasValue
                    || task.SourceExpiresAtUtc > now || ImportSourceRetentionRules.IsInUse(task.StatusKey)
                    || task.LeaseExpiresAtUtc > now) continue;
                if (task.SourceReleaseStartedAtUtc is null && await commands.ExecuteAsync(ImportSourceRetentionSql.BeginRelease,
                        ImportExportSqlParameters.Create(("Id", task.Id), ("FileId", task.SourceFileId), ("Version", task.Version), ("Now", now)), token).ConfigureAwait(false) != 1)
                    continue;
                // Files 先撤销可读状态，再删除对象；异常保留意图，同一文件允许幂等恢复。
                await files.ReleaseAsync("import_export", task.Id, task.SourceFileId, token).ConfigureAwait(false);
                completed += await commands.ExecuteAsync(ImportSourceRetentionSql.CompleteRelease,
                    ImportExportSqlParameters.Create(("Id", task.Id), ("FileId", task.SourceFileId), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { ImportSourceRetentionLog.Deferred(logger, row.Id, row.TenantId, exception); }
            finally
            {
                current.Clear();
                // 失败和停用租户同样前移，避免固定毒项占满最早一页；空尾页仅回卷一次。
                cursor.Advance(new(row.SourceExpiresAtUtc, row.Id));
            }
        }
        return completed;
    }
}

internal readonly record struct ImportSourceRetentionPosition(DateTimeOffset ExpiresAtUtc, Guid Id);

/// <summary>仅保存一个有界游标，不为每个租户保留进程内字典。</summary>
internal sealed class ImportSourceRetentionScanState
{
    private readonly object gate = new();
    private ImportSourceRetentionPosition? position;
    internal ImportSourceRetentionPosition? Read() { lock (gate) return position; }
    internal void Advance(ImportSourceRetentionPosition? value) { lock (gate) position = value; }
}

internal static partial class ImportSourceRetentionLog
{
    [LoggerMessage(EventId = 6631, Level = LogLevel.Warning, Message = "Import source cleanup deferred for task {TaskId} in tenant {TenantId}.")]
    public static partial void Deferred(ILogger logger, Guid taskId, Guid tenantId, Exception exception);

    [LoggerMessage(EventId = 6632, Level = LogLevel.Error, Message = "Import source retention iteration failed.")]
    public static partial void IterationFailed(ILogger logger, Exception exception);
}
