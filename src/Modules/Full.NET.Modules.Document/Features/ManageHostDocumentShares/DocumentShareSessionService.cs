using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.DocumentAccessLogs;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Security;
using Full.NET.Modules.Files.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentShares;

/// <summary>分享访问会话的唯一授权入口；扣次、幂等键和固定版本在本地事务内提交。</summary>
internal sealed class DocumentShareSessionService(
    IQueryExecutor queries, ICommandExecutor commands, ICommandTransaction transaction,
    DocumentMutationGuard guard, IClock clock, IIdGenerator ids, IDocumentSharePasswordHasher passwords,
    IDataProtectionProvider protection, IHostFileContentReader contentReader, DocumentAccessLogRecorder logs,
    IOptions<DocumentShareSessionOptions> options)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Full.NET.Document.ShareSession.v1");

    internal async Task<Result<HostDocumentShareAccessResponse>> OpenAsync(string code, AccessHostDocumentShareRequest request,
        string? fingerprint, CancellationToken token)
    {
        if (request.RequestKey == Guid.Empty) return Invalid<HostDocumentShareAccessResponse>();
        var share = await FindShareAsync(code, token).ConfigureAwait(false);
        if (share is null || !share.IsEnabled || share.ExpireTime <= clock.UtcNow) return Denied<HostDocumentShareAccessResponse>();
        if (!string.IsNullOrEmpty(share.PasswordHash))
        {
            if (string.IsNullOrEmpty(request.Password)) return Result<HostDocumentShareAccessResponse>.Failure(
                new Error(DocumentErrorCodes.HostSharePasswordRequired, "This share requires a password.", ErrorType.Validation));
            if (!passwords.Verify(share.Id, share.PasswordHash, request.Password)) return Denied<HostDocumentShareAccessResponse>();
        }
        var result = await transaction.ExecuteResultAsync(async ct =>
        {
            var item = await guard.LockAsync(share.DocumentId, ct).ConfigureAwait(false);
            var current = await FindShareAsync(code, ct).ConfigureAwait(false);
            if (item is null || item.DeletedAtUtc is not null || item.Status != (int)HostDocumentStatus.Published || item.VersionId is null ||
                current is null || !current.IsEnabled || current.ExpireTime <= clock.UtcNow || current.PolicyRevision != share.PolicyRevision)
                return Denied<HostDocumentShareAccessResponse>();
            var existing = await queries.QuerySingleOrDefaultAsync<DocumentShareSessionRecord>(DocumentShareSessionSql.FindByRequest,
                DocumentSqlParameters.Create(("ShareId", share.Id), ("RequestKey", request.RequestKey)), ct).ConfigureAwait(false);
            if (existing is not null && existing.ExpiresAtUtc < clock.UtcNow.AddHours(-24))
            {
                await commands.ExecuteAsync(DocumentShareSessionSql.DeleteExpired,
                    DocumentSqlParameters.Create(("Id", existing.Id), ("Before", clock.UtcNow.AddHours(-24))), ct).ConfigureAwait(false);
                existing = null;
            }
            if (existing is not null)
            {
                if (!Matches(existing, item, current)) return Denied<HostDocumentShareAccessResponse>();
                return Result<HostDocumentShareAccessResponse>.Success(Map(existing, item, current));
            }
            var now = clock.UtcNow;
            var consumed = await commands.ExecuteAsync(DocumentShareSql.TryConsumeAccess,
                DocumentSqlParameters.Create(("Id", current.Id), ("Now", now), ("Version", current.Version)), ct).ConfigureAwait(false);
            if (consumed != 1) return Result<HostDocumentShareAccessResponse>.Failure(new Error(
                DocumentErrorCodes.ShareMaxAccessReached, "Share access quota is exhausted.", ErrorType.Conflict));
            var expiry = now.AddMinutes(options.Value.LifetimeMinutes);
            if (expiry > current.ExpireTime) expiry = current.ExpireTime;
            var session = new DocumentShareSessionRecord { Id = ids.NewId(), ShareId = share.Id, DocumentId = item.Id,
                VersionId = item.VersionId.Value, RequestKey = request.RequestKey, AccessRevision = item.AccessRevision,
                PolicyRevision = current.PolicyRevision, CreatedAtUtc = now, ExpiresAtUtc = expiry };
            await commands.ExecuteAsync(DocumentShareSessionSql.Insert, DocumentSqlParameters.Create(
                ("Id", session.Id), ("ShareId", session.ShareId), ("DocumentId", session.DocumentId), ("VersionId", session.VersionId),
                ("RequestKey", session.RequestKey), ("AccessRevision", session.AccessRevision), ("PolicyRevision", session.PolicyRevision),
                ("CreatedAtUtc", now), ("ExpiresAtUtc", expiry)), ct).ConfigureAwait(false);
            // 只有新会话记录一次分享打开；同键重试不会重复扣次或增加访问事实。
            await logs.RecordInTransactionAsync(item.Id, item.Title, HostDocumentAccessTypeKeys.ShareAccess,
                HostDocumentAccessSourceKeys.Share, null, fingerprint, ct).ConfigureAwait(false);
            return Result<HostDocumentShareAccessResponse>.Success(Map(session, item, current, consumed: true));
        }, token).ConfigureAwait(false);
        return result;
    }

    internal async Task<Result<DocumentShareSessionRecord>> ResolveAsync(string code, string? credential, CancellationToken token)
    {
        var result = await ResolveSnapshotAsync(code, credential, token).ConfigureAwait(false);
        return result.IsSuccess ? Result<DocumentShareSessionRecord>.Success(result.Value!.Session)
            : Result<DocumentShareSessionRecord>.Failure(result.Error!);
    }

    private async Task<Result<AuthorizedShareSession>> ResolveSnapshotAsync(string code, string? credential, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(credential) || credential.Length > 4096) return Denied<AuthorizedShareSession>();
        Guid sessionId;
        try
        {
            if (!Guid.TryParseExact(_protector.Unprotect(credential), "N", out sessionId)) return Denied<AuthorizedShareSession>();
        }
        catch (System.Security.Cryptography.CryptographicException) { return Denied<AuthorizedShareSession>(); }
        var session = await queries.QuerySingleOrDefaultAsync<DocumentShareSessionRecord>(DocumentShareSessionSql.FindById,
            DocumentSqlParameters.Create(("Id", sessionId)), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (session is null) return Denied<AuthorizedShareSession>();
        var share = await FindShareAsync(code, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var item = await queries.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(DocumentItemSql.FindActiveById,
            DocumentSqlParameters.Create(("Id", session.DocumentId)), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return item is not null && share is not null && share.Id == session.ShareId && Matches(session, item, share)
            ? Result<AuthorizedShareSession>.Success(new(session, item.Title)) : Denied<AuthorizedShareSession>();
    }

    internal async Task<Result<HostFileContent>> ReadAsync(string code, AccessHostDocumentShareRequest request,
        string? fingerprint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.AccessTypeKey is not null && request.AccessTypeKey != HostDocumentAccessTypeKeys.Download
            && request.AccessTypeKey != HostDocumentAccessTypeKeys.Preview) return Denied<HostFileContent>();
        var authorized = await ResolveSnapshotAsync(code, request.PreviewGrant, token).ConfigureAwait(false);
        if (!authorized.IsSuccess) return Result<HostFileContent>.Failure(authorized.Error!);
        var session = authorized.Value!.Session;
        var version = await queries.QuerySingleOrDefaultAsync<DocumentVersionRecord>(DocumentItemSql.FindVersionById,
            DocumentSqlParameters.Create(("DocumentItemId", session.DocumentId), ("VersionId", session.VersionId)), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (version is null) return Denied<HostFileContent>();
        var opened = await contentReader.OpenReadyContentAsync(version.FileId, token).ConfigureAwait(false);
        if (!opened.IsSuccess)
        {
            token.ThrowIfCancellationRequested();
            return opened;
        }
        var transferred = false;
        try
        {
            var current = await ResolveSnapshotAsync(code, request.PreviewGrant, token).ConfigureAwait(false);
            if (!current.IsSuccess) return Result<HostFileContent>.Failure(current.Error!);
            await logs.RecordAsync(session.DocumentId, current.Value!.DocumentTitle, (request.AccessTypeKey ?? HostDocumentAccessTypeKeys.Download),
                HostDocumentAccessSourceKeys.Share, null, fingerprint, token, request.RequestKey == Guid.Empty ? session.Id : request.RequestKey).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            transferred = true;
            return opened;
        }
        finally
        {
            // 权威复查或日志失败时调用方拿不到流，必须在本层释放；成功后由 HTTP 层持有。
            if (!transferred) await opened.Value!.Content.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed record AuthorizedShareSession(DocumentShareSessionRecord Session, string DocumentTitle);

    private Task<DocumentShareRecord?> FindShareAsync(string code, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<DocumentShareRecord>(DocumentShareSql.FindByCode,
            DocumentSqlParameters.Create(("ShareCode", code)), token);

    private bool Matches(DocumentShareSessionRecord session, DocumentItemDetailRecord item, DocumentShareRecord share) =>
        session.ExpiresAtUtc > clock.UtcNow && share.ExpireTime > clock.UtcNow && share.IsEnabled &&
        item.DeletedAtUtc is null && item.Status == (int)HostDocumentStatus.Published && session.VersionId == item.VersionId &&
        session.AccessRevision == item.AccessRevision && session.PolicyRevision == share.PolicyRevision;

    private HostDocumentShareAccessResponse Map(DocumentShareSessionRecord session, DocumentItemDetailRecord item,
        DocumentShareRecord share, bool consumed = false) => new(share.Id, item.Id, share.ShareCode, item.Title,
            item.FileName, item.MimeType, item.FileSizeBytes ?? 0, !string.IsNullOrEmpty(share.PasswordHash),
            share.MaxAccessCount is int limit ? Math.Max(0, limit - share.AccessCount - (consumed ? 1 : 0)) : int.MaxValue)
            { PreviewGrant = _protector.Protect(session.Id.ToString("N")) };

    private static Result<T> Denied<T>() => Result<T>.Failure(new Error(DocumentErrorCodes.HostShareAccessDenied, "Share access denied.", ErrorType.NotFound));
    private static Result<T> Invalid<T>() => Result<T>.Failure(new Error(DocumentErrorCodes.ShareInvalid, "A non-empty request key is required.", ErrorType.Validation));
}

/// <summary>分享会话独立预算，不随 Office 转换超时变化。</summary>
internal sealed class DocumentShareSessionOptions
{
    public int LifetimeMinutes { get; set; } = 15;
}
