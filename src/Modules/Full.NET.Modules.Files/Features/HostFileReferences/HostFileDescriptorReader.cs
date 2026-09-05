using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.HostFileReferences;

/// <summary>向其他模块暴露 Host 文件只读描述信息。</summary>
internal sealed class HostFileDescriptorReader(IQueryExecutor queryExecutor) : IHostFileDescriptorReader
{
    /// <inheritdoc />
    public async Task<HostFileDescriptor?> GetReadyDescriptorAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                new Dictionary<string, object?> { ["FileId"] = fileId },
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? null
            : new HostFileDescriptor(
                row.Id,
                row.OriginalFileName,
                row.ContentType,
                row.SizeBytes,
                row.ContentHash,
                row.CreatedByUserId);
    }
}
