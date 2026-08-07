using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Features.HostFileReferenceClaims;

/// <summary>在 Files 本地事务内维护跨模块文件引用 claim 状态机。</summary>
internal sealed class HostFileReferenceClaimService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions) : IHostFileReferenceClaimService
{
    public Task<Result<HostFileReferenceClaimResult>> ClaimAsync(
        HostFileReferenceClaimRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => ClaimCoreAsync(request, token),
            cancellationToken);

    public Task<Result<HostFileReferenceClaimResult>> ConfirmAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => ConfirmCoreAsync(idempotencyKey, token),
            cancellationToken);

    public Task<Result<bool>> ReleaseAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => ReleaseCoreAsync(idempotencyKey, token),
            cancellationToken);

    private async Task<Result<HostFileReferenceClaimResult>> ClaimCoreAsync(
        HostFileReferenceClaimRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || string.IsNullOrWhiteSpace(request.ConsumerModule)
            || request.FileId == Guid.Empty
            || request.ConsumerReferenceId == Guid.Empty)
        {
            return InvalidClaim();
        }

        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<HostFileReferenceClaimRecord>(
                HostFileReferenceClaimSql.FindByIdempotencyKey,
                new { request.IdempotencyKey },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return MatchExisting(request, existing);
        }

        if (!await HostFileRowLocks.TryAcquireAsync(
                queryExecutor,
                databaseOptions.Value.Provider,
                request.FileId,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return FileNotFound();
        }

        var file = await queryExecutor
            .QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                new { FileId = request.FileId },
                cancellationToken)
            .ConfigureAwait(false);
        if (file is null)
        {
            return Result<HostFileReferenceClaimResult>.Failure(new Error(
                FilesErrorCodes.FileNotFound,
                "The referenced file is unavailable.",
                ErrorType.NotFound));
        }

        var now = clock.UtcNow;
        var claimId = idGenerator.NewId();
        var inserted = await commandExecutor.ExecuteAsync(
                HostFileReferenceClaimSql.InsertPending,
                new
                {
                    Id = claimId,
                    request.IdempotencyKey,
                    request.FileId,
                    request.ConsumerModule,
                    request.ConsumerReferenceId,
                    State = HostFileReferenceClaimStates.Pending,
                    file.ContentHash,
                    file.SizeBytes,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (inserted != 1)
        {
            // 删除与 Claim 竞争时，条件插入必须失败关闭，不能为已删除文件建立无保护引用。
            return FileNotFound();
        }

        return Result<HostFileReferenceClaimResult>.Success(
            new HostFileReferenceClaimResult(
                claimId,
                HostFileReferenceClaimStates.Pending,
                new HostFileReference(file.Id, file.SizeBytes, file.ContentHash)));
    }

    private async Task<Result<HostFileReferenceClaimResult>> ConfirmCoreAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<HostFileReferenceClaimRecord>(
                HostFileReferenceClaimSql.FindByIdempotencyKey,
                new { IdempotencyKey = idempotencyKey },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFoundClaim();
        }

        if (string.Equals(existing.State, HostFileReferenceClaimStates.Active, StringComparison.Ordinal))
        {
            return SuccessFromRecord(existing);
        }

        if (!string.Equals(existing.State, HostFileReferenceClaimStates.Pending, StringComparison.Ordinal))
        {
            return InvalidClaim();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                HostFileReferenceClaimSql.ConfirmPending,
                new
                {
                    IdempotencyKey = idempotencyKey,
                    PendingState = HostFileReferenceClaimStates.Pending,
                    ActiveState = HostFileReferenceClaimStates.Active,
                    Now = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 1)
        {
            return SuccessFromRecord(existing with
            {
                State = HostFileReferenceClaimStates.Active,
                ConfirmedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        var reloaded = await queryExecutor
            .QuerySingleOrDefaultAsync<HostFileReferenceClaimRecord>(
                HostFileReferenceClaimSql.FindByIdempotencyKey,
                new { IdempotencyKey = idempotencyKey },
                cancellationToken)
            .ConfigureAwait(false);
        return reloaded is not null
               && string.Equals(reloaded.State, HostFileReferenceClaimStates.Active, StringComparison.Ordinal)
            ? SuccessFromRecord(reloaded)
            : InvalidClaim();
    }

    private async Task<Result<bool>> ReleaseCoreAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                HostFileReferenceClaimSql.ReleaseOpen,
                new
                {
                    IdempotencyKey = idempotencyKey,
                    PendingState = HostFileReferenceClaimStates.Pending,
                    ActiveState = HostFileReferenceClaimStates.Active,
                    ReleasedState = HostFileReferenceClaimStates.Released,
                    Now = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 1)
        {
            return Result<bool>.Success(true);
        }

        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<HostFileReferenceClaimRecord>(
                HostFileReferenceClaimSql.FindByIdempotencyKey,
                new { IdempotencyKey = idempotencyKey },
                cancellationToken)
            .ConfigureAwait(false);
        return existing is not null
               && string.Equals(existing.State, HostFileReferenceClaimStates.Released, StringComparison.Ordinal)
            ? Result<bool>.Success(true)
            : NotFoundClaimBoolResult();
    }

    public async Task<bool> HasOpenClaimsAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var count = await queryExecutor
            .QuerySingleOrDefaultAsync<int>(
                HostFileReferenceClaimSql.CountOpenByFileId,
                new
                {
                    FileId = fileId,
                    PendingState = HostFileReferenceClaimStates.Pending,
                    ActiveState = HostFileReferenceClaimStates.Active,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return count > 0;
    }

    private static Result<HostFileReferenceClaimResult> MatchExisting(
        HostFileReferenceClaimRequest request,
        HostFileReferenceClaimRecord existing)
    {
        if (existing.FileId != request.FileId
            || !string.Equals(existing.ConsumerModule, request.ConsumerModule, StringComparison.Ordinal)
            || existing.ConsumerReferenceId != request.ConsumerReferenceId)
        {
            return Result<HostFileReferenceClaimResult>.Failure(new Error(
                FilesErrorCodes.ClaimPayloadConflict,
                "The file reference claim payload conflicts with an existing idempotency key.",
                ErrorType.Conflict));
        }

        if (string.Equals(
                existing.State,
                HostFileReferenceClaimStates.Released,
                StringComparison.Ordinal))
        {
            // Released 是终态；复用旧幂等键会让新引用失去 Pending/Active 删除保护。
            return InvalidClaim();
        }

        return SuccessFromRecord(existing);
    }

    private static Result<HostFileReferenceClaimResult> FileNotFound() =>
        Result<HostFileReferenceClaimResult>.Failure(new Error(
            FilesErrorCodes.FileNotFound,
            "The referenced file is unavailable.",
            ErrorType.NotFound));

    private static Result<HostFileReferenceClaimResult> SuccessFromRecord(
        HostFileReferenceClaimRecord record) =>
        Result<HostFileReferenceClaimResult>.Success(
            new HostFileReferenceClaimResult(
                record.Id,
                record.State,
                new HostFileReference(record.FileId, record.SizeBytes, record.ContentHash)));

    private static Result<HostFileReferenceClaimResult> InvalidClaim() =>
        Result<HostFileReferenceClaimResult>.Failure(new Error(
            FilesErrorCodes.InvalidClaim,
            "The file reference claim request is invalid.",
            ErrorType.Validation));

    private static Result<HostFileReferenceClaimResult> NotFoundClaim() =>
        Result<HostFileReferenceClaimResult>.Failure(new Error(
            FilesErrorCodes.ClaimNotFound,
            "The file reference claim was not found.",
            ErrorType.NotFound));

    private static Result<bool> NotFoundClaimBoolResult() =>
        Result<bool>.Failure(new Error(
            FilesErrorCodes.ClaimNotFound,
            "The file reference claim was not found.",
            ErrorType.NotFound));
}
