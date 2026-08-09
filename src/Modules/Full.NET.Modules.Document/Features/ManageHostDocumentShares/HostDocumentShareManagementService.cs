using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentShares;

internal sealed class HostDocumentShareManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentShareQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string ShareCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public Task<Result<HostDocumentShareResponse>> CreateAsync(
        CreateHostDocumentShareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DocumentId == Guid.Empty || request.ValidDays < 1 || request.ValidDays > 365)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteResultAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);
    }

    public Task<Result<HostDocumentShareResponse>> UpdateStatusAsync(
        Guid shareId,
        UpdateHostDocumentShareStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteResultAsync(
            token => UpdateStatusCoreAsync(shareId, request, token),
            cancellationToken);
    }

    public async Task<Result<HostDocumentShareResponse>> AccessByCodeAsync(
        string shareCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shareCode))
        {
            return Invalid();
        }

        var shareResult = await queries.GetByCodeAsync(shareCode, cancellationToken)
            .ConfigureAwait(false);

        if (!shareResult.IsSuccess)
        {
            return shareResult;
        }

        var share = shareResult.Value!;

        if (clock.UtcNow > share.ExpireTime)
        {
            return Expired();
        }

        if (!share.IsEnabled)
        {
            return Disabled();
        }

        if (share.MaxAccessCount.HasValue && share.AccessCount >= share.MaxAccessCount.Value)
        {
            return MaxAccessReached();
        }

        await commandExecutor.ExecuteAsync(
                DocumentShareSql.IncrementAccessCount,
                new { share.Id },
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(share.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostDocumentShareResponse>> CreateCoreAsync(
        CreateHostDocumentShareRequest request,
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

        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        var shareCode = GenerateShareCode();

        await commandExecutor.ExecuteAsync(
                DocumentShareSql.Insert,
                new
                {
                    Id = id,
                    DocumentId = request.DocumentId,
                    ShareCode = shareCode,
                    CreatedAtUtc = now,
                    ExpireTime = now.AddDays(request.ValidDays),
                    Password = request.Password,
                    MaxAccessCount = request.MaxAccessCount,
                    Version = 1L,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostDocumentShareResponse>> UpdateStatusCoreAsync(
        Guid shareId,
        UpdateHostDocumentShareStatusRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentShareRecord>(
                DocumentShareSql.FindById,
                new { Id = shareId },
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return NotFound();
        }

        var affected = await commandExecutor.ExecuteAsync(
                DocumentShareSql.UpdateStatus,
                new
                {
                    Id = shareId,
                    IsEnabled = request.IsEnabled ? 1 : 0,
                    Version = request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (affected != 1)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(shareId, cancellationToken).ConfigureAwait(false);
    }

    private static string GenerateShareCode()
    {
        var chars = new char[12];
        var random = new Random();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = ShareCodeChars[random.Next(ShareCodeChars.Length)];
        }
        return new string(chars);
    }

    private static Result<HostDocumentShareResponse> Invalid() =>
        Result<HostDocumentShareResponse>.Failure(InvalidError());

    private static Result<HostDocumentShareResponse> NotFound() =>
        Result<HostDocumentShareResponse>.Failure(NotFoundError());

    private static Result<HostDocumentShareResponse> DocumentNotFound() =>
        Result<HostDocumentShareResponse>.Failure(DocumentNotFoundError());

    private static Result<HostDocumentShareResponse> VersionConflict() =>
        Result<HostDocumentShareResponse>.Failure(VersionConflictError());

    private static Result<HostDocumentShareResponse> Expired() =>
        Result<HostDocumentShareResponse>.Failure(ExpiredError());

    private static Result<HostDocumentShareResponse> Disabled() =>
        Result<HostDocumentShareResponse>.Failure(DisabledError());

    private static Result<HostDocumentShareResponse> MaxAccessReached() =>
        Result<HostDocumentShareResponse>.Failure(MaxAccessReachedError());

    private static Error InvalidError() =>
        new(DocumentErrorCodes.ShareInvalid, "The document share request is invalid.", ErrorType.Validation);

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.ShareNotFound, "Document share was not found.", ErrorType.NotFound);

    private static Error DocumentNotFoundError() =>
        new(DocumentErrorCodes.NotFound, "The document for sharing was not found.", ErrorType.NotFound);

    private static Error VersionConflictError() =>
        new(DocumentErrorCodes.ShareVersionConflict, "Document share was updated by another operation.", ErrorType.Conflict);

    private static Error ExpiredError() =>
        new(DocumentErrorCodes.ShareExpired, "The share link has expired.", ErrorType.BusinessRule);

    private static Error DisabledError() =>
        new(DocumentErrorCodes.ShareDisabled, "The share link is disabled.", ErrorType.BusinessRule);

    private static Error MaxAccessReachedError() =>
        new(DocumentErrorCodes.ShareMaxAccessReached, "The share link has reached its maximum access count.", ErrorType.BusinessRule);
}
