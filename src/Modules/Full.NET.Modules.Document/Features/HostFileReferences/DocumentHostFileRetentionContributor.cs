using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;

/// <summary>Document 模块保留策略贡献点；向 Files/Document 等宿主注册清理范围。</summary>
namespace Full.NET.Modules.Document.Features.HostFileReferences;

internal sealed class DocumentHostFileRetentionContributor(IQueryExecutor queryExecutor, IClock clock)
    : IHostFileRetentionContributor
{
    public async Task<bool> IsFileReferencedAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var referenced = await queryExecutor
            .QuerySingleOrDefaultAsync<int>(
                DocumentItemSql.IsFileReferenced,
                DocumentSqlParameters.Create(("FileId", fileId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (referenced == 1) return true;
        return await queryExecutor.QuerySingleOrDefaultAsync<int>(DocumentPreviewReferenceSql.FileReferenced,
            DocumentSqlParameters.Create(("FileId", fileId), ("Now", clock.UtcNow)), cancellationToken).ConfigureAwait(false) == 1;
    }
}
