using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.ManageHostFolders;

/// <summary>Host 虚拟目录树只读查询。</summary>
internal sealed class HostFolderQueryService(IQueryExecutor queryExecutor, IOptions<DatabaseOptions> databaseOptions)
{
    internal const int MaximumDepth = 32;
    internal const int MaximumChildren = 1000;

    /// <summary>只读取当前父节点的一层，超预算明确拒绝，不把整棵树放入内存或 JSON 递归。</summary>
    public async Task<Result<IReadOnlyList<HostFolderTreeNode>>> GetTreeAsync(
        Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mysql = databaseOptions.Value.Provider == DatabaseProvider.MySql;
        var rows = await queryExecutor.QueryAsync<HostFolderChildRecord>(
            mysql ? HostFolderSql.ListChildrenMySql : HostFolderSql.ListChildrenSqlServer,
            new Dictionary<string, object?> { ["ParentId"] = parentId, ["TakeCount"] = MaximumChildren + 1 },
            cancellationToken).ConfigureAwait(false);
        if (rows.Count > MaximumChildren)
            return Result<IReadOnlyList<HostFolderTreeNode>>.Failure(new Error(FilesErrorCodes.FolderQueryLimitExceeded,
                "Too many child folders; narrow the folder scope.", ErrorType.BusinessRule));
        return Result<IReadOnlyList<HostFolderTreeNode>>.Success(rows.Select(row => new HostFolderTreeNode(
            row.Id, row.ParentId, row.Name, row.DisplayOrder, row.Revision, row.HasChildren)).ToArray());
    }

    /// <summary>祖先链最多读取深度上限；父关系不可变，新增节点不能绕过已有层级预算。</summary>
    internal Task<int> GetDepthAsync(Guid folderId, CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<int>(databaseOptions.Value.Provider == DatabaseProvider.MySql
                ? HostFolderSql.AncestorDepthMySql : HostFolderSql.AncestorDepthSqlServer,
            new Dictionary<string, object?> { ["FolderId"] = folderId, ["MaxDepth"] = MaximumDepth }, cancellationToken);

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

    private static Result<T> FolderNotFound<T>() =>
        Result<T>.Failure(new Error(
            FilesErrorCodes.FolderNotFound,
            "The folder was not found.",
            ErrorType.NotFound));
}
