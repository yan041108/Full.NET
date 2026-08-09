using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPermissions;

internal sealed class HostDocumentPermissionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentPermissionQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<IReadOnlyList<HostDocumentPermissionResponse>>> SetPermissionsAsync(
        SetHostDocumentPermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DocumentId == Guid.Empty || request.Permissions is null)
        {
            return Task.FromResult(Invalid());
        }

        foreach (var perm in request.Permissions)
        {
            if (perm.UserId == Guid.Empty || string.IsNullOrWhiteSpace(perm.PermissionLevel))
            {
                return Task.FromResult(Invalid());
            }
        }

        return transaction.ExecuteResultAsync(
            token => SetPermissionsCoreAsync(request, token),
            cancellationToken);
    }

    private async Task<Result<IReadOnlyList<HostDocumentPermissionResponse>>> SetPermissionsCoreAsync(
        SetHostDocumentPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var document = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemRecord>(
                DocumentItemSql.FindActiveById,
                new { Id = request.DocumentId },
                cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return DocumentNotFound();
        }

        await commandExecutor.ExecuteAsync(
                DocumentPermissionSql.DeleteByDocument,
                new { DocumentId = request.DocumentId },
                cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        foreach (var perm in request.Permissions)
        {
            var id = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                    DocumentPermissionSql.Insert,
                    new
                    {
                        Id = id,
                        DocumentId = request.DocumentId,
                        UserId = perm.UserId,
                        PermissionLevel = perm.PermissionLevel.Trim(),
                        CreatedAtUtc = now,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.ListByDocumentAsync(request.DocumentId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Result<IReadOnlyList<HostDocumentPermissionResponse>> Invalid() =>
        Result<IReadOnlyList<HostDocumentPermissionResponse>>.Failure(InvalidError());

    private static Result<IReadOnlyList<HostDocumentPermissionResponse>> DocumentNotFound() =>
        Result<IReadOnlyList<HostDocumentPermissionResponse>>.Failure(DocumentNotFoundError());

    private static Error InvalidError() =>
        new(DocumentErrorCodes.PermissionInvalid, "The permission request is invalid.", ErrorType.Validation);

    private static Error DocumentNotFoundError() =>
        new(DocumentErrorCodes.PermissionDocumentNotFound, "The document for permissions was not found.", ErrorType.NotFound);
}
