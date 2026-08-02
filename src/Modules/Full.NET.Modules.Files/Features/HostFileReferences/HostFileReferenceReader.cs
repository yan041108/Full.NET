using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.HostFileReferences;

/// <summary>向其他模块暴露 Host 文件只读引用校验。</summary>
internal sealed class HostFileReferenceReader(IQueryExecutor queryExecutor) : IHostFileReferenceReader
{
    public async Task<HostFileReference?> GetReadyReferenceAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                new { FileId = fileId },
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? null
            : new HostFileReference(row.Id, row.SizeBytes, row.ContentHash);
    }
}
