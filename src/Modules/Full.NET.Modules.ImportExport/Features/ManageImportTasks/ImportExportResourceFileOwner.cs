using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Persistence;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>通过本模块租户 SQL 确认文件引用，供 Files 兼容存量引用和清理对账。</summary>
/// <param name="queries">受租户守卫约束的查询执行器。</param>
internal sealed class ImportExportResourceFileOwner(IQueryExecutor queries) : ITenantResourceFileOwner
{
    /// <inheritdoc />
    public string OwnerModuleKey => "import_export";

    /// <inheritdoc />
    public async Task<bool> IsReferencedAsync(Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var record = await queries.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById,
            new Dictionary<string, object?> { ["Id"] = resourceId }, cancellationToken).ConfigureAwait(false);
        return record is not null && (record.SourceFileId == fileId || record.ErrorReceiptFileId == fileId);
    }
}
