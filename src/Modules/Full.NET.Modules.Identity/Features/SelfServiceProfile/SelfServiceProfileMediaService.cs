using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Features.HostFileReferences;

namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

/// <summary>当前用户头像与签名媒体绑定；通过 Files Port/Claim 管理引用，禁止任意路径或 URL 写回。</summary>
internal sealed class SelfServiceProfileMediaService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    SelfServiceProfileService profileService,
    ICurrentTenantContextWriter currentTenantWriter,
    IHostFileUploadWriter? hostFileUploadWriter = null,
    IHostFileReferenceClaimService? hostFileReferenceClaimService = null,
    IHostFileDescriptorReader? hostFileDescriptorReader = null,
    IHostFileContentReader? hostFileContentReader = null,
    IOutboxWriter? outboxWriter = null)
{
    private IOutboxWriter? Outbox => outboxWriter;
    /// <summary>上传并绑定头像。</summary>
    public Task<Result<SelfServiceProfileResponse>> UploadAvatarAsync(
        Guid userId,
        string actorScope,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default) =>
        IdentityHostExecutionScope.RunAsync(
            currentTenantWriter,
            () => UploadAsync(
                userId,
                actorScope,
                SelfServiceProfileMediaKind.Avatar,
                fileName,
                contentType,
                content,
                contentLength,
                cancellationToken));

    /// <summary>上传并绑定签名图。</summary>
    public Task<Result<SelfServiceProfileResponse>> UploadSignatureAsync(
        Guid userId,
        string actorScope,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default) =>
        IdentityHostExecutionScope.RunAsync(
            currentTenantWriter,
            () => UploadAsync(
                userId,
                actorScope,
                SelfServiceProfileMediaKind.Signature,
                fileName,
                contentType,
                content,
                contentLength,
                cancellationToken));

    /// <summary>解除头像绑定。</summary>
    public Task<Result<SelfServiceProfileResponse>> DeleteAvatarAsync(
        Guid userId,
        string actorScope,
        CancellationToken cancellationToken = default) =>
        IdentityHostExecutionScope.RunAsync(
            currentTenantWriter,
            () => DeleteAsync(userId, actorScope, SelfServiceProfileMediaKind.Avatar, cancellationToken));

    /// <summary>解除签名绑定。</summary>
    public Task<Result<SelfServiceProfileResponse>> DeleteSignatureAsync(
        Guid userId,
        string actorScope,
        CancellationToken cancellationToken = default) =>
        IdentityHostExecutionScope.RunAsync(
            currentTenantWriter,
            () => DeleteAsync(userId, actorScope, SelfServiceProfileMediaKind.Signature, cancellationToken));

    /// <summary>打开当前用户头像内容流。</summary>
    public Task<Result<HostFileContent>> OpenAvatarContentAsync(
        Guid userId,
        string actorScope,
        CancellationToken cancellationToken = default) =>
        IdentityHostExecutionScope.RunAsync(
            currentTenantWriter,
            () => OpenContentAsync(userId, actorScope, SelfServiceProfileMediaKind.Avatar, cancellationToken));

    /// <summary>打开当前用户签名内容流。</summary>
    public Task<Result<HostFileContent>> OpenSignatureContentAsync(
        Guid userId,
        string actorScope,
        CancellationToken cancellationToken = default) =>
        IdentityHostExecutionScope.RunAsync(
            currentTenantWriter,
            () => OpenContentAsync(userId, actorScope, SelfServiceProfileMediaKind.Signature, cancellationToken));

    private async Task<Result<SelfServiceProfileResponse>> UploadAsync(
        Guid userId,
        string actorScope,
        SelfServiceProfileMediaKind kind,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken)
    {
        if (!SelfServiceProfilePolicy.IsHostActorScope(actorScope))
        {
            return HostOnlyFailure<SelfServiceProfileResponse>();
        }

        // 基础预设允许不装配 Files；媒体依赖不完整时，必须在上传及数据库访问前拒绝。
        if (hostFileUploadWriter is null || hostFileReferenceClaimService is null || hostFileDescriptorReader is null || Outbox is null)
        {
            return MediaInvalid<SelfServiceProfileResponse>();
        }

        if (!IsAllowedContentType(kind, contentType))
        {
            return MediaInvalid<SelfServiceProfileResponse>();
        }

        if (contentLength <= 0 || contentLength > GetMaxBytes(kind))
        {
            return MediaInvalid<SelfServiceProfileResponse>();
        }

        var user = await FindActiveUserAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized<SelfServiceProfileResponse>();
        }

        var uploadResult = await hostFileUploadWriter.UploadAsync(
                userId,
                fileName,
                contentType,
                content,
                contentLength,
                cancellationToken)
            .ConfigureAwait(false);
        if (!uploadResult.IsSuccess)
        {
            return Result<SelfServiceProfileResponse>.Failure(uploadResult.Error!);
        }

        var fileId = uploadResult.Value!.FileId;
        var descriptor = await hostFileDescriptorReader
            .GetReadyDescriptorAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
        if (descriptor is null
            || descriptor.CreatedByUserId != userId
            || descriptor.SizeBytes > GetMaxBytes(kind)
            || !IsAllowedContentType(kind, descriptor.ContentType))
        {
            return MediaInvalid<SelfServiceProfileResponse>();
        }

        var profile = await LoadProfileAsync(userId, cancellationToken).ConfigureAwait(false);
        var previousFileId = kind == SelfServiceProfileMediaKind.Avatar
            ? profile?.AvatarFileId
            : profile?.SignatureFileId;
        var idempotencyKey = kind == SelfServiceProfileMediaKind.Avatar
            ? HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(userId, fileId, Guid.CreateVersion7())
            : HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(userId, fileId, Guid.CreateVersion7());
        var previousKey = previousFileId is Guid previous
            ? ResolveClaimKey(kind, userId, previous, profile!) : null;
        if (previousKey is not null && Outbox is null) return MediaInvalid<SelfServiceProfileResponse>();
        var claimResult = await hostFileReferenceClaimService
            .ClaimAsync(
                new HostFileReferenceClaimRequest(
                    idempotencyKey,
                    HostFileReferenceClaimConsumerModules.Identity,
                    userId,
                    fileId),
                cancellationToken)
            .ConfigureAwait(false);
        if (!claimResult.IsSuccess)
        {
            return Result<SelfServiceProfileResponse>.Failure(claimResult.Error!);
        }

        try
        {
            var bindResult = await transaction.ExecuteResultAsync(
                    async token =>
                    {
                        var result = await BindMediaAsync(userId, kind, fileId, idempotencyKey, profile?.Version, token).ConfigureAwait(false);
                        if (result.IsSuccess && previousKey is not null)
                            await IdentityProfileMediaUnboundOutbox.PublishAsync(Outbox!, userId, previousFileId!.Value,
                                MediaKindName(kind), previousKey, token).ConfigureAwait(false);
                        return result;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!bindResult.IsSuccess)
            {
                await hostFileReferenceClaimService
                    .ReleaseAsync(idempotencyKey, cancellationToken)
                    .ConfigureAwait(false);
                return Result<SelfServiceProfileResponse>.Failure(bindResult.Error!);
            }
        }
        catch
        {
            throw;
        }

        var confirmResult = await hostFileReferenceClaimService
            .ConfirmAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (!confirmResult.IsSuccess)
        {
            return Result<SelfServiceProfileResponse>.Failure(confirmResult.Error!);
        }

        if (previousKey is not null)
        {
            await ReleaseMediaClaimAsync(previousKey!, cancellationToken)
                .ConfigureAwait(false);
        }

        return await profileService.GetAsync(userId, actorScope, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> BindMediaAsync(
        Guid userId,
        SelfServiceProfileMediaKind kind,
        Guid fileId,
        string claimKey,
        int? version,
        CancellationToken cancellationToken)
    {
        var statement = kind switch
        {
            SelfServiceProfileMediaKind.Avatar => IdentitySql.UpdateHostUserProfileAvatar,
            SelfServiceProfileMediaKind.Signature => IdentitySql.UpdateHostUserProfileSignature,
            _ => throw new InvalidOperationException("Unsupported media kind."),
        };
        if (version is int observedVersion)
        {
            // 上传期间档案可能被其他媒体或普通资料编辑；只能替换原快照中的绑定。
            var affected = await commandExecutor.ExecuteAsync(
                statement,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("Version", observedVersion),
                    ("ClaimKey", claimKey),
                    kind == SelfServiceProfileMediaKind.Avatar
                        ? ("AvatarFileId", fileId)
                        : ("SignatureFileId", fileId)),
                cancellationToken)
            .ConfigureAwait(false);
            return affected == 1 ? Result<bool>.Success(true) : Result<bool>.Failure(new Error(
                IdentityErrorCodes.ProfileVersionConflict,
                "The host user profile was updated concurrently.",
                ErrorType.Conflict));
        }

        try
        {
            // 首次快照为空时直接插入；不能先 UPDATE 并覆盖上传期间已经创建的档案。
            var inserted = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertHostUserProfileMediaShell,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("AvatarFileId", kind == SelfServiceProfileMediaKind.Avatar ? fileId : null),
                    ("SignatureFileId", kind == SelfServiceProfileMediaKind.Signature ? fileId : null),
                    ("AvatarClaimKey", kind == SelfServiceProfileMediaKind.Avatar ? claimKey : null),
                    ("SignatureClaimKey", kind == SelfServiceProfileMediaKind.Signature ? claimKey : null)),
                cancellationToken)
            .ConfigureAwait(false);
            return inserted == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(new Error(
                IdentityErrorCodes.ProfileVersionConflict,
                "The host user profile was updated concurrently.",
                ErrorType.Conflict));
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.ProfileVersionConflict,
                "The host user profile was updated concurrently.",
                ErrorType.Conflict));
        }
    }

    private async Task<Result<SelfServiceProfileResponse>> DeleteAsync(
        Guid userId,
        string actorScope,
        SelfServiceProfileMediaKind kind,
        CancellationToken cancellationToken)
    {
        if (!SelfServiceProfilePolicy.IsHostActorScope(actorScope))
        {
            return HostOnlyFailure<SelfServiceProfileResponse>();
        }

        // 缺少引用释放能力时保留已有绑定，避免解除档案后遗留孤立引用。
        if (hostFileReferenceClaimService is null)
        {
            return MediaInvalid<SelfServiceProfileResponse>();
        }

        var user = await FindActiveUserAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized<SelfServiceProfileResponse>();
        }

        var profile = await LoadProfileAsync(userId, cancellationToken).ConfigureAwait(false);
        var fileId = kind == SelfServiceProfileMediaKind.Avatar
            ? profile?.AvatarFileId
            : profile?.SignatureFileId;
        if (fileId is null)
        {
            return await profileService.GetAsync(userId, actorScope, cancellationToken)
                .ConfigureAwait(false);
        }

        var statement = kind == SelfServiceProfileMediaKind.Avatar
            ? IdentitySql.ClearHostUserProfileAvatar
            : IdentitySql.ClearHostUserProfileSignature;
        // 删除只对读取的文件与档案版本生效，冲突不能清除赢家或释放任何引用。
        if (Outbox is null) return MediaInvalid<SelfServiceProfileResponse>();
        var releaseKey = ResolveClaimKey(kind, userId, fileId.Value, profile!);
        var clearResult = await transaction.ExecuteResultAsync(async token =>
        {
            var affected = await commandExecutor.ExecuteAsync(
                    statement,
                    IdentitySqlParameters.Create(("UserId", userId), ("FileId", fileId.Value), ("Version", profile!.Version)),
                    token)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                return Result<bool>.Failure(new Error(
                    IdentityErrorCodes.ProfileVersionConflict,
                    "The host user profile was updated concurrently.",
                    ErrorType.Conflict));
            }
            await IdentityProfileMediaUnboundOutbox.PublishAsync(Outbox, userId, fileId.Value, MediaKindName(kind), releaseKey, token).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, cancellationToken).ConfigureAwait(false);
        if (!clearResult.IsSuccess) return Result<SelfServiceProfileResponse>.Failure(clearResult.Error!);
        await ReleaseMediaClaimAsync(releaseKey, cancellationToken)
            .ConfigureAwait(false);
        return await profileService.GetAsync(userId, actorScope, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostFileContent>> OpenContentAsync(
        Guid userId,
        string actorScope,
        SelfServiceProfileMediaKind kind,
        CancellationToken cancellationToken)
    {
        if (!SelfServiceProfilePolicy.IsHostActorScope(actorScope))
        {
            return HostOnlyFailure<HostFileContent>();
        }

        if (hostFileContentReader is null)
        {
            return MediaNotFound<HostFileContent>();
        }

        var user = await FindActiveUserAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized<HostFileContent>();
        }

        var profile = await LoadProfileAsync(userId, cancellationToken).ConfigureAwait(false);
        var fileId = kind == SelfServiceProfileMediaKind.Avatar
            ? profile?.AvatarFileId
            : profile?.SignatureFileId;
        if (fileId is null)
        {
            return MediaNotFound<HostFileContent>();
        }

        var exists = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                IdentitySql.HostUserProfileMediaExists,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("FileId", fileId.Value),
                    ("Kind", kind == SelfServiceProfileMediaKind.Avatar ? "avatar" : "signature")),
                cancellationToken)
            .ConfigureAwait(false);
        if (exists != 1)
        {
            return MediaNotFound<HostFileContent>();
        }

        var content = await hostFileContentReader
            .OpenReadyContentAsync(fileId.Value, cancellationToken)
            .ConfigureAwait(false);
        return content.IsSuccess
            ? content
            : MediaNotFound<HostFileContent>();
    }

    private async Task ReleaseMediaClaimAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _ = await hostFileReferenceClaimService!
            .ReleaseAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string MediaKindName(SelfServiceProfileMediaKind kind) => kind == SelfServiceProfileMediaKind.Avatar ? "avatar" : "signature";

    private static string ResolveClaimKey(SelfServiceProfileMediaKind kind, Guid userId, Guid fileId, HostUserProfileRecord profile)
    {
        var legacy = BuildIdempotencyKey(kind, userId, fileId);
        var stored = kind == SelfServiceProfileMediaKind.Avatar ? profile.AvatarClaimKey : profile.SignatureClaimKey;
        // 旧版本可能只更换文件列；残留的另一文件代际不能作为当前文件的释放凭据。
        return stored is not null && HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, stored) ? stored : legacy;
    }

    private Task<IdentityUserRecord?> FindActiveUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
            IdentitySql.FindHostUserById,
            IdentitySqlParameters.Create(("UserId", userId)),
            cancellationToken);

    private async Task<HostUserProfileRecord?> LoadProfileAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        (await queryExecutor.QueryAsync<HostUserProfileRecord>(
                IdentitySql.ListHostUserProfilesByIds,
                IdentitySqlParameters.Create(("UserIds", new[] { userId })),
                cancellationToken)
            .ConfigureAwait(false))
        .FirstOrDefault();

    private static string BuildIdempotencyKey(
        SelfServiceProfileMediaKind kind,
        Guid userId,
        Guid fileId) =>
        kind switch
        {
            SelfServiceProfileMediaKind.Avatar =>
                HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(userId, fileId),
            SelfServiceProfileMediaKind.Signature =>
                HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(userId, fileId),
            _ => throw new InvalidOperationException("Unsupported media kind."),
        };

    private static bool IsAllowedContentType(SelfServiceProfileMediaKind kind, string contentType) =>
        kind switch
        {
            SelfServiceProfileMediaKind.Avatar =>
                SelfServiceProfileMediaPolicy.IsAllowedAvatarContentType(contentType),
            SelfServiceProfileMediaKind.Signature =>
                SelfServiceProfileMediaPolicy.IsAllowedSignatureContentType(contentType),
            _ => false,
        };

    private static long GetMaxBytes(SelfServiceProfileMediaKind kind) =>
        kind switch
        {
            SelfServiceProfileMediaKind.Avatar => SelfServiceProfileMediaPolicy.AvatarMaxBytes,
            SelfServiceProfileMediaKind.Signature => SelfServiceProfileMediaPolicy.SignatureMaxBytes,
            _ => 0,
        };

    private static Result<T> HostOnlyFailure<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.SelfServiceProfileHostOnly,
            "Self-service profile is only available in the host actor scope.",
            ErrorType.Forbidden));

    private static Result<T> Unauthorized<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.SessionNotActive,
            "The current session is no longer active.",
            ErrorType.Unauthorized));

    private static Result<T> MediaInvalid<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.SelfServiceProfileMediaInvalid,
            "The uploaded media file is invalid for this profile field.",
            ErrorType.Validation));

    private static Result<T> MediaNotFound<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.SelfServiceProfileMediaNotFound,
            "The requested profile media is not available.",
            ErrorType.NotFound));
}
