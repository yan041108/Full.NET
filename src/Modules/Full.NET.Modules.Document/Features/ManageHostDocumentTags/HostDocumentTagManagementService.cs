using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentTags;

internal sealed class HostDocumentTagManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentTagQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<HostDocumentTagResponse>> CreateAsync(
        CreateHostDocumentTagRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(request.Name, out var name))
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteAsync(
            token => CreateCoreAsync(name, token),
            cancellationToken);
    }

    public Task<Result<HostDocumentTagResponse>> UpdateAsync(
        Guid tagId,
        UpdateHostDocumentTagRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(request.Name, out var name) || request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteAsync(
            token => UpdateCoreAsync(tagId, name, request.Version, token),
            cancellationToken);
    }

    public Task<Result<bool>> DeleteAsync(
        Guid tagId,
        Guid actorUserId,
        DeleteHostDocumentTagRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(Result<bool>.Failure(InvalidError()));
        }

        return transaction.ExecuteAsync(
            token => DeleteCoreAsync(tagId, actorUserId, request.Version, token),
            cancellationToken);
    }

    private async Task<Result<HostDocumentTagResponse>> CreateCoreAsync(
        string name,
        CancellationToken cancellationToken)
    {
        if (await FindNameConflictAsync(name, null, cancellationToken).ConfigureAwait(false))
        {
            return NameExists();
        }

        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                DocumentTagSql.Insert,
                new
                {
                    Id = id,
                    Name = name,
                    CreatedAtUtc = now,
                    Version = 1,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostDocumentTagResponse>> UpdateCoreAsync(
        Guid tagId,
        string name,
        long version,
        CancellationToken cancellationToken)
    {
        if (await queries.GetByIdAsync(tagId, cancellationToken).ConfigureAwait(false) is { IsSuccess: false })
        {
            return NotFound();
        }

        if (await FindNameConflictAsync(name, tagId, cancellationToken).ConfigureAwait(false))
        {
            return NameExists();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                DocumentTagSql.Update,
                new
                {
                    Id = tagId,
                    Name = name,
                    UpdatedAtUtc = now,
                    Version = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(tagId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid tagId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        if (await queries.GetByIdAsync(tagId, cancellationToken).ConfigureAwait(false) is { IsSuccess: false })
        {
            return Result<bool>.Failure(NotFoundError());
        }

        var assignmentCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                DocumentTagSql.CountAssignments,
                new { TagId = tagId },
                cancellationToken)
            .ConfigureAwait(false);
        if (assignmentCount > 0)
        {
            return Result<bool>.Failure(InUseError());
        }

        var affected = await commandExecutor.ExecuteAsync(
                DocumentTagSql.SoftDelete,
                new
                {
                    Id = tagId,
                    DeletedAtUtc = clock.UtcNow,
                    DeletedByUserId = actorUserId,
                    Version = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(VersionConflictError());
    }

    private async Task<bool> FindNameConflictAsync(
        string name,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentNameConflictRecord>(
                DocumentTagSql.FindActiveByName,
                new { Name = name },
                cancellationToken)
            .ConfigureAwait(false);
        return existing is not null && existing.Id != excludeId;
    }

    private static bool TryNormalizeName(string? value, out string name)
    {
        name = value?.Trim() ?? string.Empty;
        return name.Length is >= 1 and <= 64;
    }

    private static Result<HostDocumentTagResponse> Invalid() =>
        Result<HostDocumentTagResponse>.Failure(InvalidError());

    private static Result<HostDocumentTagResponse> NotFound() =>
        Result<HostDocumentTagResponse>.Failure(NotFoundError());

    private static Result<HostDocumentTagResponse> NameExists() =>
        Result<HostDocumentTagResponse>.Failure(
            new Error(DocumentErrorCodes.TagNameExists, "Tag name already exists.", ErrorType.Conflict));

    private static Result<HostDocumentTagResponse> VersionConflict() =>
        Result<HostDocumentTagResponse>.Failure(VersionConflictError());

    private static Error InvalidError() =>
        new(DocumentErrorCodes.TagInvalid, "The tag request is invalid.", ErrorType.Validation);

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.TagNotFound, "Document tag was not found.", ErrorType.NotFound);

    private static Error VersionConflictError() =>
        new(DocumentErrorCodes.TagVersionConflict, "Tag was updated by another operation.", ErrorType.Conflict);

    private static Error InUseError() =>
        new(DocumentErrorCodes.TagInUse, "Tag is assigned to documents.", ErrorType.BusinessRule);
}
