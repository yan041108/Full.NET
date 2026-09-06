using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.ManageHostFolders;

/// <summary>Host 虚拟目录树只读查询。</summary>
internal sealed class HostFolderQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<IReadOnlyList<HostFolderTreeNode>>> GetTreeAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<HostFolderRecord>(
                HostFolderSql.ListActive,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<HostFolderTreeNode>>.Success(BuildTree(rows));
    }

    public async Task<Result<HostFolderResponse>> GetByIdAsync(
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostFolderRecord>(
                HostFolderSql.FindActiveById,
                new Dictionary<string, object?> { ["FolderId"] = folderId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? FolderNotFound<HostFolderResponse>()
            : Result<HostFolderResponse>.Success(Map(record));
    }

    public async Task<bool> ExistsActiveAsync(
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var count = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostFolderSql.ExistsActiveById,
                new Dictionary<string, object?> { ["FolderId"] = folderId },
                cancellationToken)
            .ConfigureAwait(false);
        return count > 0;
    }

    internal static HostFolderResponse Map(HostFolderRecord record) =>
        new(
            record.Id,
            record.ParentId,
            record.Name,
            record.DisplayOrder,
            record.Revision,
            record.CreatedAtUtc,
            record.CreatedByUserId,
            record.UpdatedAtUtc,
            record.UpdatedByUserId);

    private static IReadOnlyList<HostFolderTreeNode> BuildTree(
        IEnumerable<HostFolderRecord> rows)
    {
        var allRows = rows.ToArray();
        var childrenByParent = allRows
            .Where(row => row.ParentId is not null)
            .GroupBy(row => row.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        HostFolderTreeNode BuildNode(HostFolderRecord record)
        {
            var childRows = childrenByParent.TryGetValue(record.Id, out var matches)
                ? matches
                : Array.Empty<HostFolderRecord>();
            var children = childRows
                .OrderBy(child => child.DisplayOrder)
                .ThenBy(child => child.Name, StringComparer.Ordinal)
                .ThenBy(child => child.Id)
                .Select(BuildNode)
                .ToArray();
            return new HostFolderTreeNode(
                record.Id,
                record.ParentId,
                record.Name,
                record.DisplayOrder,
                record.Revision,
                children);
        }

        return allRows
            .Where(row => row.ParentId is null)
            .OrderBy(root => root.DisplayOrder)
            .ThenBy(root => root.Name, StringComparer.Ordinal)
            .ThenBy(root => root.Id)
            .Select(BuildNode)
            .ToArray();
    }

    private static Result<T> FolderNotFound<T>() =>
        Result<T>.Failure(new Error(
            FilesErrorCodes.FolderNotFound,
            "The folder was not found.",
            ErrorType.NotFound));
}
