using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Features.DocumentAccessLogs;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Security;
using Full.NET.Modules.Files.Contracts;
using System.Security.Cryptography;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentShares;

/// <summary>
/// Host 文档分享的管理与匿名访问服务。承担以下安全边界：
/// 1) ShareCode 由 <see cref="RandomNumberGenerator"/>.GetString 从 62 字符表生成 12 位，
///    熵约 71.4 位，禁止使用可预测的 <c>System.Random</c>；
/// 2) 口令以 PBKDF2 不可逆哈希存储，投影与响应中永不包含明文或哈希；
/// 3) 匿名访问计数必须由 <see cref="DocumentShareSql.TryConsumeAccess"/> 在单条 UPDATE 内原子完成，
///    禁止在内存层做 MaxAccessCount 预检后再 UPDATE——会引入 TOCTOU 漏洞；
/// 4) 无效凭据统一拒绝；缺少口令允许交互补填，额度耗尽返回冲突，内容入口只接受会话凭据。
/// </summary>
internal sealed class HostDocumentShareManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentShareQueryService queries,
    HostDocumentPreviewTaskManagementService previewTaskManagement,
    HostDocumentPreviewTaskQueryService previewTaskQueries,
    IClock clock,
    IIdGenerator idGenerator,
    IDocumentSharePasswordHasher passwordHasher,
    IDocumentAccessPolicy accessPolicy,
    DocumentShareSessionService sessions,
    DocumentMutationGuard guard,
    DocumentAccessLogRecorder accessLogs)
{
    /// <summary>ShareCode 字符表：大写字母+小写字母+数字共 62 个字符，保证 URL 安全且无歧义。</summary>
    private const string ShareCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public Task<Result<HostDocumentShareResponse>> CreateAsync(
        CreateHostDocumentShareRequest request,
        CancellationToken cancellationToken = default) =>
        accessPolicy.AuthorizeAsync(request.DocumentId, DocumentOperation.Share,
            () => CreateAuthorizedAsync(request, cancellationToken), cancellationToken);

    private Task<Result<HostDocumentShareResponse>> CreateAuthorizedAsync(
        CreateHostDocumentShareRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateShareCreateRequest(
            request.DocumentId,
            request.ValidDays,
            request.Password);
        if (!validation.IsSuccess)
        {
            return Task.FromResult(Result<HostDocumentShareResponse>.Failure(validation.Error!));
        }

        return transaction.ExecuteResultAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);
    }

    /// <summary>批量创建分享；逐文档独立事务，允许部分成功。</summary>
    public async Task<Result<BatchCreateHostDocumentSharesResponse>> BatchCreateAsync(
        BatchCreateHostDocumentSharesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DocumentIds is null
            || request.DocumentIds.Count == 0
            || request.DocumentIds.Count > HostDocumentShareBatchLimits.MaxDocumentCount)
        {
            return InvalidBatch();
        }

        if (request.DocumentIds.Any(id => id == Guid.Empty))
        {
            return InvalidBatch();
        }

        var validation = ValidateSharePolicy(request.ValidDays, request.Password);
        if (!validation.IsSuccess)
        {
            return Result<BatchCreateHostDocumentSharesResponse>.Failure(validation.Error!);
        }

        var results = new List<BatchCreateHostDocumentShareItem>(request.DocumentIds.Count);
        var succeeded = 0;
        foreach (var documentId in request.DocumentIds)
        {
            var single = new CreateHostDocumentShareRequest(
                documentId,
                request.ValidDays,
                request.Password,
                request.MaxAccessCount);
            var createResult = await CreateAsync(single, cancellationToken)
                .ConfigureAwait(false);
            if (createResult.IsSuccess)
            {
                succeeded++;
                results.Add(new BatchCreateHostDocumentShareItem(
                    documentId,
                    true,
                    createResult.Value,
                    null,
                    null));
            }
            else
            {
                results.Add(new BatchCreateHostDocumentShareItem(
                    documentId,
                    false,
                    null,
                    createResult.Error!.Code,
                    createResult.Error.Message));
            }
        }

        return Result<BatchCreateHostDocumentSharesResponse>.Success(
            new BatchCreateHostDocumentSharesResponse(succeeded, results));
    }

    private static Result<bool> ValidateShareCreateRequest(
        Guid documentId,
        int validDays,
        string? password)
    {
        if (documentId == Guid.Empty)
        {
            return Result<bool>.Failure(InvalidError());
        }

        return ValidateSharePolicy(validDays, password);
    }

    private static Result<bool> ValidateSharePolicy(int validDays, string? password)
    {
        if (validDays < 1 || validDays > 365)
        {
            return Result<bool>.Failure(InvalidError());
        }

        if (!string.IsNullOrEmpty(password)
            && (password.Length < 8 || password.Length > 128))
        {
            return Result<bool>.Failure(PasswordInvalidLengthError());
        }

        return Result<bool>.Success(true);
    }

    private static Result<BatchCreateHostDocumentSharesResponse> InvalidBatch() =>
        Result<BatchCreateHostDocumentSharesResponse>.Failure(InvalidError());

    public async Task<Result<HostDocumentShareResponse>> UpdateStatusAsync(
        Guid shareId,
        UpdateHostDocumentShareStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Invalid();
        }

        var share = await queryExecutor.QuerySingleOrDefaultAsync<DocumentShareRecord>(DocumentShareSql.FindById,
            DocumentSqlParameters.Create(("Id", shareId)), cancellationToken).ConfigureAwait(false);
        if (share is null) return NotFound();
        return await transaction.ExecuteResultAsync(
            token => UpdateStatusCoreAsync(shareId, share.DocumentId, request, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 匿名口令校验访问：返回文档元数据。
    /// 新会话在本地事务内原子扣次并绑定版本；同键重试复用结果。
    /// </summary>
    public async Task<Result<HostDocumentShareAccessResponse>> AccessAnonymousAsync(
        string shareCode,
        AccessHostDocumentShareRequest request,
        string? clientIpFingerprint,
        CancellationToken cancellationToken = default)
    {
        return await sessions.OpenAsync(shareCode, request, clientIpFingerprint, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 匿名分享内容读取：校验会话凭据与权威修订后返回绑定版本文件。
    /// 每次验证会话绑定的版本及权威策略，不重复消耗打开额度。
    /// </summary>
    public async Task<Result<HostFileContent>> OpenAnonymousContentAsync(
        string shareCode,
        AccessHostDocumentShareRequest request,
        string? clientIpFingerprint,
        CancellationToken cancellationToken = default)
    {
        return await sessions.ReadAsync(shareCode, request, clientIpFingerprint, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>凭分享会话提交固定版本的 Office 预览任务，同一会话复用任务标识。</summary>
    public async Task<Result<HostDocumentPreviewTaskResponse>> CreateAnonymousPreviewTaskAsync(
        string shareCode,
        AccessHostDocumentShareRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await sessions.ResolveAsync(shareCode, request.PreviewGrant, cancellationToken).ConfigureAwait(false);
        if (!session.IsSuccess) return Result<HostDocumentPreviewTaskResponse>.Failure(session.Error!);
        var created = await previewTaskManagement.CreateAuthorizedAsync(
            new CreateHostDocumentPreviewTaskRequest(session.Value!.DocumentId, session.Value.VersionId), null,
            cancellationToken, session.Value.Id).ConfigureAwait(false);
        if (!created.IsSuccess) return created;
        var current = await sessions.ResolveAsync(shareCode, request.PreviewGrant, cancellationToken).ConfigureAwait(false);
        return current.IsSuccess ? Result<HostDocumentPreviewTaskResponse>.Success(created.Value! with
            { PreviewGrant = request.PreviewGrant, PreviewPollUntilUtc = current.Value!.ExpiresAtUtc })
            : Result<HostDocumentPreviewTaskResponse>.Failure(current.Error!);
    }

    /// <summary>匿名读取分享关联的预览任务状态；任务必须属于分享绑定的文档。</summary>
    public async Task<Result<HostDocumentPreviewTaskResponse>> GetAnonymousPreviewTaskAsync(
        string shareCode,
        Guid taskId,
        AccessHostDocumentShareRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await sessions.ResolveAsync(shareCode, request.PreviewGrant, cancellationToken).ConfigureAwait(false);
        if (!session.IsSuccess || session.Value!.Id != taskId) return Result<HostDocumentPreviewTaskResponse>.Failure(AccessDeniedError());
        var task = await previewTaskQueries.GetAuthorizedAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (!task.IsSuccess) return task;
        if (task.Value!.DocumentItemId != session.Value.DocumentId || task.Value.VersionId != session.Value.VersionId)
            return Result<HostDocumentPreviewTaskResponse>.Failure(AccessDeniedError());
        var current = await sessions.ResolveAsync(shareCode, request.PreviewGrant, cancellationToken).ConfigureAwait(false);
        return current.IsSuccess ? task : Result<HostDocumentPreviewTaskResponse>.Failure(current.Error!);
    }

    /// <summary>匿名读取分享关联且已完成的预览任务输出内容。</summary>
    public async Task<Result<HostFileContent>> OpenAnonymousPreviewTaskContentAsync(
        string shareCode,
        Guid taskId,
        AccessHostDocumentShareRequest request,
        CancellationToken cancellationToken = default)
    {
        var task = await GetAnonymousPreviewTaskAsync(shareCode, taskId, request, cancellationToken).ConfigureAwait(false);
        if (!task.IsSuccess) return Result<HostFileContent>.Failure(task.Error!);
        var content = await previewTaskQueries.OpenOutputContentAuthorizedAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (!content.IsSuccess) return content;
        var transferred = false;
        try
        {
            var current = await sessions.ResolveAsync(shareCode, request.PreviewGrant, cancellationToken).ConfigureAwait(false);
            if (!current.IsSuccess) return Result<HostFileContent>.Failure(current.Error!);
            await accessLogs.RecordAsync(task.Value!.DocumentItemId, task.Value.DocumentTitle, HostDocumentAccessTypeKeys.Preview,
                HostDocumentAccessSourceKeys.Share, null, null, cancellationToken, current.Value!.Id).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            transferred = true;
            return content;
        }
        finally
        {
            if (!transferred) await content.Value!.Content.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Result<HostDocumentShareResponse>> CreateCoreAsync(
        CreateHostDocumentShareRequest request,
        CancellationToken cancellationToken)
    {
        var document = await guard.LockAsync(request.DocumentId, cancellationToken).ConfigureAwait(false);
        if (await accessPolicy.CheckAsync(request.DocumentId, DocumentOperation.Share, cancellationToken).ConfigureAwait(false) is { } denied)
            return Result<HostDocumentShareResponse>.Failure(denied);
        if (document is null || document.DeletedAtUtc is not null || document.IsPurging) return DocumentNotFound();
        if (document.Status != (int)HostDocumentStatus.Published || document.VersionId is null || request.MaxAccessCount is <= 0)
            return Result<HostDocumentShareResponse>.Failure(new Error(DocumentErrorCodes.ShareInvalid, "Only published documents with content can be shared.", ErrorType.Validation));
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
                DocumentSqlParameters.Create(("Id", id), ("DocumentId", request.DocumentId), ("ShareCode", shareCode), ("CreatedAtUtc", now), ("ExpireTime", now.AddDays(request.ValidDays)), ("PasswordHash", passwordHash), ("MaxAccessCount", request.MaxAccessCount), ("Version", 1L)),
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostDocumentShareResponse>> UpdateStatusCoreAsync(
        Guid shareId,
        Guid documentId,
        UpdateHostDocumentShareStatusRequest request,
        CancellationToken cancellationToken)
    {
        var document = await guard.LockAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (document is null || document.DeletedAtUtc is not null) return NotFound();
        if (request.IsEnabled && (document.Status != (int)HostDocumentStatus.Published || document.VersionId is null))
            return Result<HostDocumentShareResponse>.Failure(new Error(DocumentErrorCodes.VersionConflict,
                "Only published documents can enable shares.", ErrorType.Conflict));
        if (await accessPolicy.CheckAsync(documentId, DocumentOperation.Share, cancellationToken).ConfigureAwait(false) is { } error)
            return Result<HostDocumentShareResponse>.Failure(error);

        var affected = await commandExecutor.ExecuteAsync(
                DocumentShareSql.UpdateStatus,
                DocumentSqlParameters.Create(("Id", shareId), ("IsEnabled", request.IsEnabled ? 1 : 0), ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected != 1)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(shareId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 生成分享码：使用密码学安全随机源从 62 字符表取 12 位，
    /// 熵约 71.4 位；禁止替换为 <c>System.Random</c> 或缩减长度。
    /// </summary>
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

    private static Result<HostFileContent> AccessDeniedContent() =>
        Result<HostFileContent>.Failure(AccessDeniedError());

    private static Result<HostFileContent> AccessMaxReachedContent() =>
        Result<HostFileContent>.Failure(MaxAccessReachedError());

    private static Result<(DocumentShareRecord, bool)> AccessDeniedTuple() =>
        Result<(DocumentShareRecord, bool)>.Failure(AccessDeniedError());

    private static Result<(DocumentShareRecord, bool)> PasswordRequiredTuple() =>
        Result<(DocumentShareRecord, bool)>.Failure(PasswordRequiredError());

    private static Result<DocumentShareRecord> AccessDeniedRecord() =>
        Result<DocumentShareRecord>.Failure(AccessDeniedError());

    private static Result<DocumentShareRecord> AccessMaxReachedRecord() =>
        Result<DocumentShareRecord>.Failure(MaxAccessReachedError());

    private static Result<HostDocumentShareAccessResponse> MapTupleFailureToAccess(
        Result<(DocumentShareRecord, bool)> failure) =>
        failure.Error!.Code switch
        {
            DocumentErrorCodes.HostSharePasswordRequired => PasswordRequired(),
            DocumentErrorCodes.ShareMaxAccessReached => AccessMaxReached(),
            _ => AccessDenied(),
        };

    private static Result<HostFileContent> MapShareFailureToContent<T>(Result<T> failure) =>
        failure.Error!.Code switch
        {
            DocumentErrorCodes.HostSharePasswordRequired => Result<HostFileContent>.Failure(PasswordRequiredError()),
            DocumentErrorCodes.ShareMaxAccessReached => AccessMaxReachedContent(),
            _ => AccessDeniedContent(),
        };

    private static Result<HostDocumentPreviewTaskResponse> MapShareFailureToPreviewTask<T>(Result<T> failure) =>
        failure.Error!.Code switch
        {
            DocumentErrorCodes.HostSharePasswordRequired => Result<HostDocumentPreviewTaskResponse>.Failure(
                PasswordRequiredError()),
            DocumentErrorCodes.ShareMaxAccessReached => Result<HostDocumentPreviewTaskResponse>.Failure(
                MaxAccessReachedError()),
            _ => Result<HostDocumentPreviewTaskResponse>.Failure(AccessDeniedError()),
        };

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
