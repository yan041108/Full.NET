using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>通过本模块租户 SQL 确认文件引用，供 Files 兼容存量引用和清理对账。</summary>
/// <param name="queries">受租户守卫约束的查询执行器。</param>
/// <param name="databaseOptions">当前数据库提供程序。</param>
internal sealed class ReportingResourceFileOwner(
    IQueryExecutor queries,
    IOptions<DatabaseOptions> databaseOptions) : ITenantResourceFileOwner
{
    /// <inheritdoc />
    public string OwnerModuleKey => "reporting";

    /// <inheritdoc />
    public async Task<bool> IsReferencedAsync(Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var record = await queries.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindByIdFor(databaseOptions.Value.Provider),
                new Dictionary<string, object?> { ["Id"] = resourceId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is not null && (record.OutputFileId == fileId);
    }
}
