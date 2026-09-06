using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.ManageHostFolders;

/// <summary>Host 虚拟目录创建、更新与删除；目录名不参与存储键推导。</summary>
internal sealed class HostFolderManagementService(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor,
    HostFolderQueryService folderQueries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<Result<HostFolderResponse>> CreateAsync(
        Guid createdByUserId,
        CreateHostFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(request.Name, out var normalizedName))
        {
            return InvalidFolder("Folder name is required.");
        }

        if (request.ParentId is Guid parentId)
        {
            if (!await folderQueries.ExistsActiveAsync(parentId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return FolderNotFound();
            }
        }

        var folderId = idGenerator.NewId();
        var now = clock.UtcNow;
        try
        {
            var affected = await commandExecutor.ExecuteAsync(
                    HostFolderSql.Insert,
                    new Dictionary<string, object?>
                    {
                        ["Id"] = folderId,
                        ["ParentId"] = request.ParentId,
                        ["Name"] = normalizedName,
                        ["DisplayOrder"] = request.DisplayOrder,
                        ["CreatedAtUtc"] = now,
                        ["CreatedByUserId"] = createdByUserId,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                throw new InvalidOperationException(
                    "Files folder insert must affect exactly one row.");
            }
        }
        catch (Exception ex) when (IsUniqueNameViolation(ex))
        {
            return NameConflict();
        }

        return await folderQueries.GetByIdAsync(folderId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<HostFolderResponse>> UpdateAsync(
        Guid folderId,
        Guid updatedByUserId,
        UpdateHostFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ExpectedRevision < 0
            || !TryNormalizeName(request.Name, out var normalizedName))
        {
            return InvalidFolder("Folder update request is invalid.");
        }

        var existing = await folderQueries.GetByIdAsync(folderId, cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }

        if (existing.Value!.Revision != request.ExpectedRevision)
        {
            return RevisionConflict();
        }

        var now = clock.UtcNow;
        try
        {
            var affected = await commandExecutor.ExecuteAsync(
                    HostFolderSql.Update,
                    new Dictionary<string, object?>
                    {
                        ["FolderId"] = folderId,
                        ["Name"] = normalizedName,
                        ["DisplayOrder"] = request.DisplayOrder,
                        ["ExpectedRevision"] = request.ExpectedRevision,
                        ["UpdatedAtUtc"] = now,
                        ["UpdatedByUserId"] = updatedByUserId,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected == 0)
            {
                return await ResolveMutationFailureAsync(folderId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsUniqueNameViolation(ex))
        {
            return NameConflict();
        }

        return await folderQueries.GetByIdAsync(folderId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<HostFolderResponse>> DeleteAsync(
        Guid folderId,
        Guid deletedByUserId,
        DeleteHostFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ExpectedRevision < 0)
        {
            return InvalidFolder("Folder delete request is invalid.");
        }

        var existing = await folderQueries.GetByIdAsync(folderId, cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }

        if (existing.Value!.Revision != request.ExpectedRevision)
        {
            return RevisionConflict();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                HostFolderSql.SoftDelete,
                new Dictionary<string, object?>
                {
                    ["FolderId"] = folderId,
                    ["ExpectedRevision"] = request.ExpectedRevision,
                    ["DeletedAtUtc"] = now,
                    ["UpdatedByUserId"] = deletedByUserId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 1)
        {
            return Result<HostFolderResponse>.Success(existing.Value);
        }

        return await ResolveDeleteFailureAsync(folderId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostFolderResponse>> ResolveMutationFailureAsync(
        Guid folderId,
        CancellationToken cancellationToken)
    {
        var current = await folderQueries.GetByIdAsync(folderId, cancellationToken)
            .ConfigureAwait(false);
        return current.IsSuccess
            ? RevisionConflict()
            : FolderNotFound();
    }

    private async Task<Result<HostFolderResponse>> ResolveDeleteFailureAsync(
        Guid folderId,
        CancellationToken cancellationToken)
    {
        var current = await folderQueries.GetByIdAsync(folderId, cancellationToken)
            .ConfigureAwait(false);
        if (!current.IsSuccess)
        {
            return FolderNotFound();
        }

        var childCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            HostFolderSql.CountActiveChildren,
            new Dictionary<string, object?> { ["FolderId"] = folderId },
            cancellationToken).ConfigureAwait(false);
        var fileCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            HostFolderSql.CountActiveFiles,
            new Dictionary<string, object?> { ["FolderId"] = folderId },
            cancellationToken).ConfigureAwait(false);
        if (childCount > 0 || fileCount > 0)
        {
            return FolderNotEmpty();
        }

        return RevisionConflict();
    }

    private static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = name?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= 128;
    }

    private static bool IsUniqueNameViolation(Exception exception) =>
        exception.Message.Contains("UX_fn_files_folder_ParentId_Name", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase);

    private static Result<HostFolderResponse> InvalidFolder(string message) =>
        Result<HostFolderResponse>.Failure(new Error(
            FilesErrorCodes.InvalidFolder,
            message,
            ErrorType.Validation));

    private static Result<HostFolderResponse> FolderNotFound() =>
        Result<HostFolderResponse>.Failure(new Error(
            FilesErrorCodes.FolderNotFound,
            "The folder was not found.",
            ErrorType.NotFound));

    private static Result<HostFolderResponse> NameConflict() =>
        Result<HostFolderResponse>.Failure(new Error(
            FilesErrorCodes.FolderNameConflict,
            "A folder with the same name already exists under the parent.",
            ErrorType.Conflict));

    private static Result<HostFolderResponse> FolderNotEmpty() =>
        Result<HostFolderResponse>.Failure(new Error(
            FilesErrorCodes.FolderNotEmpty,
            "The folder still contains child folders or files.",
            ErrorType.Conflict));

    private static Result<HostFolderResponse> RevisionConflict() =>
        Result<HostFolderResponse>.Failure(new Error(
            FilesErrorCodes.RevisionConflict,
            "The folder revision is out of date.",
            ErrorType.Conflict));
}
