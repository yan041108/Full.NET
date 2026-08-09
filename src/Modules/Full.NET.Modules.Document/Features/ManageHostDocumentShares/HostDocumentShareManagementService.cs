using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Security;
using System.Security.Cryptography;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentShares;

internal sealed class HostDocumentShareManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentShareQueryService queries,
    IClock clock,
    IIdGenerator idGenerator,
    IDocumentSharePasswordHasher passwordHasher)
{
    private const string ShareCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public Task<Result<HostDocumentShareResponse>> CreateAsync(
        CreateHostDocumentShareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DocumentId == Guid.Empty
            || request.ValidDays < 1
            || request.ValidDays > 365)
        {
            return Task.FromResult(Invalid());
        }

        // 口令长度校验：8–128 字符；空字符串视同未设置。
        if (!string.IsNullOrEmpty(request.Password)
            && (request.Password.Length < 8 || request.Password.Length > 128))
        {
            return Task.FromResult(PasswordInvalidLength());
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

    /// <summary>
    /// 匿名口令校验访问：返回文档元数据。
    /// 错误路径（分享不存在/口令错误/过期禁用等）统一返回 HostShareAccessDenied，
    /// 避免通过耗时或错误码侧信道泄露存在性信息；成功才在事务内原子计数。
    /// </summary>
    public async Task<Result<HostDocumentShareAccessResponse>> AccessAnonymousAsync(
        string shareCode,
        AccessHostDocumentShareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shareCode))
        {
            return AccessDenied();
        }

        var share = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentShareRecord>(
                DocumentShareSql.FindByCode,
                new { ShareCode = shareCode },
                cancellationToken)
            .ConfigureAwait(false);

        if (share is null)
        {
            return AccessDenied();
        }

        // 中文注释：口令验证必须在过期/禁用/访问上限检查之前执行，
        // 以保证未授权用户无法通过耗时差异推断出分享存在与否。
        var hasPassword = !string.IsNullOrEmpty(share.PasswordHash);
        if (hasPassword)
        {
            if (string.IsNullOrEmpty(request.Password))
            {
                return PasswordRequired();
            }
            if (!passwordHasher.Verify(share.Id, share.PasswordHash!, request.Password!))
            {
                return AccessDenied();
            }
        }

        var now = clock.UtcNow;
        if (now > share.ExpireTime || !share.IsEnabled)
        {
            return AccessDenied();
        }
        if (share.MaxAccessCount.HasValue && share.AccessCount >= share.MaxAccessCount.Value)
        {
            return AccessMaxReached();
        }

        // 中文注释：只有验证通过、权限有效的情况下才执行原子计数自增；
        // 错误口令永不进入计数，避免被并发利用做存在性 oracle。
        var affected = await commandExecutor.ExecuteAsync(
                DocumentShareSql.IncrementAccessCount,
                new { share.Id },
                cancellationToken)
            .ConfigureAwait(false);

        var document = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById,
                new { share.DocumentId },
                cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return AccessDenied();
        }

        var remaining = share.MaxAccessCount.HasValue
            ? Math.Max(0, share.MaxAccessCount.Value - share.AccessCount - 1)
            : int.MaxValue;
        // 中文注释：匿名响应中的文件名/MIME/大小优先取版本元数据；如果版本尚未上传，
        // 用标题作为文件名占位、大小为 0，避免返回 default 导致客户端 NRE。
        var fileName = document.FileName;
        var mimeType = document.MimeType;
        var fileSizeBytes = document.FileSizeBytes ?? 0L;
        return Result<HostDocumentShareAccessResponse>.Success(new HostDocumentShareAccessResponse(
            share.Id,
            document.Id,
            share.ShareCode,
            document.Title,
            fileName,
            mimeType,
            fileSizeBytes,
            hasPassword,
            remaining));
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

        string? passwordHash = null;
        if (!string.IsNullOrEmpty(request.Password))
        {
            passwordHash = passwordHasher.Hash(id, request.Password);
        }

        await commandExecutor.ExecuteAsync(
                DocumentShareSql.Insert,
                new
                {
                    Id = id,
                    DocumentId = request.DocumentId,
                    ShareCode = shareCode,
                    CreatedAtUtc = now,
                    ExpireTime = now.AddDays(request.ValidDays),
                    PasswordHash = passwordHash,
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
        // 分享码属于匿名访问凭据，必须由密码学安全随机源生成，不能使用可预测的 Random。
        return RandomNumberGenerator.GetString(ShareCodeChars, 12);
    }

    private static Result<HostDocumentShareResponse> Invalid() =>
        Result<HostDocumentShareResponse>.Failure(InvalidError());

    private static Result<HostDocumentShareAccessResponse> AccessDenied() =>
        Result<HostDocumentShareAccessResponse>.Failure(AccessDeniedError());

    private static Result<HostDocumentShareAccessResponse> PasswordRequired() =>
        Result<HostDocumentShareAccessResponse>.Failure(PasswordRequiredError());

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

    private static Result<HostDocumentShareAccessResponse> AccessMaxReached() =>
        Result<HostDocumentShareAccessResponse>.Failure(MaxAccessReachedError());

    private static Result<HostDocumentShareResponse> PasswordInvalidLength() =>
        Result<HostDocumentShareResponse>.Failure(PasswordInvalidLengthError());

    private static Error InvalidError() =>
        new(DocumentErrorCodes.ShareInvalid, "The document share request is invalid.", ErrorType.Validation);

    private static Error AccessDeniedError() =>
        new(DocumentErrorCodes.HostShareAccessDenied, "Share access denied.", ErrorType.NotFound);

    private static Error PasswordRequiredError() =>
        new(DocumentErrorCodes.HostSharePasswordRequired, "This share requires a password.", ErrorType.Validation);

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

    private static Error PasswordInvalidLengthError() =>
        new(DocumentErrorCodes.SharePasswordInvalidLength,
            "Password must be between 8 and 128 characters in length.",
            ErrorType.Validation);
}
