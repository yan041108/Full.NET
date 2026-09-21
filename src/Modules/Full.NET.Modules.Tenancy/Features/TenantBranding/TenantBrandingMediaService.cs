using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.TenantBranding;

/// <summary>租户 Logo 媒体绑定；仅允许 Files 模块 claim-confirm 流程，禁止 URL/路径写回。</summary>
internal sealed class TenantBrandingMediaService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    ICurrentTenantContextWriter currentTenantWriter,
    IHostFileUploadWriter hostFileUploadWriter,
    IHostFileReferenceClaimService hostFileReferenceClaimService,
    IHostFileDescriptorReader hostFileDescriptorReader,
    IHostFileContentReader hostFileContentReader,
    TenantBrandingService brandingService,
    TenantCacheInvalidator cacheInvalidator)
{
    /// <summary>Host 作用域上传并绑定租户 Logo。</summary>
    public Task<Result<TenantBrandingResponse>> UploadLogoByTenantIdAsync(
        Guid tenantId,
        Guid uploadedByUserId,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default) =>
        UploadLogoAsync(
            tenantId,
            uploadedByUserId,
            TenantSql.UpdateTenantLogo,
            useCurrentTenantScope: false,
            fileName,
            contentType,
            content,
            contentLength,
            cancellationToken);

    /// <summary>当前租户上下文上传并绑定 Logo。</summary>
    public Task<Result<TenantBrandingResponse>> UploadLogoCurrentAsync(
        Guid tenantId,
        Guid uploadedByUserId,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default) =>
        UploadLogoAsync(
            tenantId,
            uploadedByUserId,
            TenantSql.UpdateTenantLogoCurrent,
            useCurrentTenantScope: true,
            fileName,
            contentType,
            content,
            contentLength,
            cancellationToken);

    /// <summary>Host 作用域删除租户 Logo。</summary>
    public Task<Result<TenantBrandingResponse>> DeleteLogoByTenantIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        DeleteLogoAsync(
            tenantId,
            TenantSql.ClearTenantLogo,
            useCurrentTenantScope: false,
            cancellationToken);

    /// <summary>当前租户上下文删除 Logo。</summary>
    public Task<Result<TenantBrandingResponse>> DeleteLogoCurrentAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        DeleteLogoAsync(
            tenantId,
            TenantSql.ClearTenantLogoCurrent,
            useCurrentTenantScope: true,
            cancellationToken);

    /// <summary>按租户标识打开 Logo 内容流。</summary>
    public Task<Result<HostFileContent>> OpenLogoContentAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        OpenContentAsync(tenantId, useCurrentTenantScope: false, cancellationToken);

    /// <summary>当前租户上下文打开 Logo 内容流。</summary>
    public Task<Result<HostFileContent>> OpenCurrentLogoContentAsync(
        CancellationToken cancellationToken = default) =>
        OpenContentAsync(useCurrentTenantScope: true, cancellationToken);

    private async Task<Result<TenantBrandingResponse>> UploadLogoAsync(
        Guid tenantId,
        Guid uploadedByUserId,
        SqlStatement updateStatement,
        bool useCurrentTenantScope,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken)
    {
        if (!TenantBrandingPolicy.IsAllowedLogoContentType(contentType)
            || contentLength <= 0
            || contentLength > TenantBrandingPolicy.LogoMaxBytes)
        {
            return LogoInvalid<TenantBrandingResponse>();
        }

        var branding = await LoadBrandingRecordAsync(
                tenantId,
                useCurrentTenantScope,
                cancellationToken)
            .ConfigureAwait(false);
        if (branding is null)
        {
            return NotFound<TenantBrandingResponse>();
        }

        var previousFileId = branding.LogoFileId;
        var claimPhase = await RunHostFileOpsIfNeededAsync(
                useCurrentTenantScope,
                async () =>
                {
                    var uploadResult = await hostFileUploadWriter
                        .UploadAsync(
                            uploadedByUserId,
                            fileName,
                            contentType,
                            content,
                            contentLength,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!uploadResult.IsSuccess)
                    {
                        return Result<(Guid FileId, string IdempotencyKey)>.Failure(uploadResult.Error!);
                    }

                    var fileId = uploadResult.Value!.FileId;
                    var descriptor = await hostFileDescriptorReader
                        .GetReadyDescriptorAsync(fileId, cancellationToken)
                        .ConfigureAwait(false);
                    if (descriptor is null
                        || descriptor.CreatedByUserId != uploadedByUserId
                        || descriptor.SizeBytes > TenantBrandingPolicy.LogoMaxBytes
                        || !TenantBrandingPolicy.IsAllowedLogoContentType(descriptor.ContentType))
                    {
                        return LogoInvalid<(Guid FileId, string IdempotencyKey)>();
                    }

                    var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(
                        tenantId,
                        fileId);
                    var claimResult = await hostFileReferenceClaimService
                        .ClaimAsync(
                            new HostFileReferenceClaimRequest(
                                idempotencyKey,
                                HostFileReferenceClaimConsumerModules.Tenancy,
                                tenantId,
                                fileId),
                            cancellationToken)
                        .ConfigureAwait(false);
                    return claimResult.IsSuccess
                        ? Result<(Guid FileId, string IdempotencyKey)>.Success((fileId, idempotencyKey))
                        : Result<(Guid FileId, string IdempotencyKey)>.Failure(claimResult.Error!);
                })
            .ConfigureAwait(false);
        if (!claimPhase.IsSuccess)
        {
            return Result<TenantBrandingResponse>.Failure(claimPhase.Error!);
        }

        var (fileId, idempotencyKey) = claimPhase.Value;

        var bindResult = await transaction.ExecuteResultAsync(
                token => BindLogoAsync(
                    tenantId,
                    fileId,
                    branding.Version,
                    updateStatement,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!bindResult.IsSuccess)
        {
            await RunHostFileOpsIfNeededAsync(
                    useCurrentTenantScope,
                    () => hostFileReferenceClaimService.ReleaseAsync(idempotencyKey, cancellationToken))
                .ConfigureAwait(false);
            return Result<TenantBrandingResponse>.Failure(bindResult.Error!);
        }

        var confirmResult = await RunHostFileOpsIfNeededAsync(
                useCurrentTenantScope,
                () => hostFileReferenceClaimService.ConfirmAsync(idempotencyKey, cancellationToken))
            .ConfigureAwait(false);
        if (!confirmResult.IsSuccess)
        {
            return Result<TenantBrandingResponse>.Failure(confirmResult.Error!);
        }

        if (previousFileId is Guid oldFileId && oldFileId != fileId)
        {
            await ReleaseLogoClaimAsync(tenantId, oldFileId, useCurrentTenantScope, cancellationToken)
                .ConfigureAwait(false);
        }

        await InvalidateBrandingCacheAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return await ReloadBrandingResponseAsync(tenantId, useCurrentTenantScope, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantBrandingResponse>> DeleteLogoAsync(
        Guid tenantId,
        SqlStatement clearStatement,
        bool useCurrentTenantScope,
        CancellationToken cancellationToken)
    {
        var branding = await LoadBrandingRecordAsync(
                tenantId,
                useCurrentTenantScope,
                cancellationToken)
            .ConfigureAwait(false);
        if (branding is null)
        {
            return NotFound<TenantBrandingResponse>();
        }

        if (branding.LogoFileId is not Guid fileId)
        {
            return await ReloadBrandingResponseAsync(tenantId, useCurrentTenantScope, cancellationToken)
                .ConfigureAwait(false);
        }

        _ = await commandExecutor.ExecuteAsync(
                clearStatement,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("UpdatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        await ReleaseLogoClaimAsync(tenantId, fileId, useCurrentTenantScope, cancellationToken)
            .ConfigureAwait(false);
        await InvalidateBrandingCacheAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return await ReloadBrandingResponseAsync(tenantId, useCurrentTenantScope, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostFileContent>> OpenContentAsync(
        Guid tenantId,
        bool useCurrentTenantScope,
        CancellationToken cancellationToken)
    {
        var branding = await LoadBrandingRecordAsync(
                tenantId,
                useCurrentTenantScope,
                cancellationToken)
            .ConfigureAwait(false);
        if (branding?.LogoFileId is not Guid fileId)
        {
            return LogoNotFound<HostFileContent>();
        }

        var exists = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                TenantSql.TenantLogoExists,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("FileId", fileId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (exists != 1)
        {
            return LogoNotFound<HostFileContent>();
        }

        var content = await RunHostFileOpsIfNeededAsync(
                useCurrentTenantScope,
                () => hostFileContentReader.OpenReadyContentAsync(fileId, cancellationToken))
            .ConfigureAwait(false);
        return content.IsSuccess
            ? content
            : LogoNotFound<HostFileContent>();
    }

    private Task<TenantBrandingRecord?> LoadBrandingRecordAsync(
        Guid tenantId,
        bool useCurrentTenantScope,
        CancellationToken cancellationToken) =>
        useCurrentTenantScope
            ? queryExecutor.QuerySingleOrDefaultAsync<TenantBrandingRecord>(
                TenantSql.FindTenantBrandingCurrent,
                TenancySqlParameters.Create(),
                cancellationToken)
            : queryExecutor.QuerySingleOrDefaultAsync<TenantBrandingRecord>(
                TenantSql.FindTenantBrandingById,
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken);

    private Task<Result<TenantBrandingResponse>> ReloadBrandingResponseAsync(
        Guid tenantId,
        bool useCurrentTenantScope,
        CancellationToken cancellationToken) =>
        useCurrentTenantScope
            ? brandingService.GetCurrentAsync(cancellationToken)
            : brandingService.GetByTenantIdAsync(tenantId, cancellationToken);

    private Task<Result<HostFileContent>> OpenContentAsync(
        bool useCurrentTenantScope,
        CancellationToken cancellationToken) =>
        OpenContentAsync(Guid.Empty, useCurrentTenantScope, cancellationToken);

    private async Task<Result<bool>> BindLogoAsync(
        Guid tenantId,
        Guid fileId,
        int version,
        SqlStatement updateStatement,
        CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
                updateStatement,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("LogoFileId", fileId),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(new Error(
                TenancyErrorCodes.VersionConflict,
                "The tenant record was updated concurrently.",
                ErrorType.Conflict));
    }

    private Task ReleaseLogoClaimAsync(
        Guid tenantId,
        Guid fileId,
        bool useCurrentTenantScope,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(tenantId, fileId);
        return RunHostFileOpsIfNeededAsync(
            useCurrentTenantScope,
            async () =>
            {
                _ = await hostFileReferenceClaimService
                    .ReleaseAsync(idempotencyKey, cancellationToken)
                    .ConfigureAwait(false);
            });
    }

    private Task<T> RunHostFileOpsIfNeededAsync<T>(
        bool useCurrentTenantScope,
        Func<Task<T>> action) =>
        useCurrentTenantScope
            ? TenancyHostExecutionScope.RunAsync(currentTenantWriter, action)
            : action();

    private Task RunHostFileOpsIfNeededAsync(
        bool useCurrentTenantScope,
        Func<Task> action) =>
        useCurrentTenantScope
            ? TenancyHostExecutionScope.RunAsync(currentTenantWriter, action)
            : action();

    private async Task InvalidateBrandingCacheAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await queryExecutor
            .QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (tenant is not null)
        {
            await cacheInvalidator.InvalidateAfterCommitAsync(
                    tenant.Id,
                    tenant.Domain,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            TenancyErrorCodes.NotFound,
            "The tenant was not found.",
            ErrorType.NotFound));

    private static Result<T> LogoInvalid<T>() =>
        Result<T>.Failure(new Error(
            TenancyErrorCodes.BrandingLogoInvalid,
            "The uploaded logo file is invalid for tenant branding.",
            ErrorType.Validation));

    private static Result<T> LogoNotFound<T>() =>
        Result<T>.Failure(new Error(
            TenancyErrorCodes.BrandingLogoNotFound,
            "The requested tenant logo is not available.",
            ErrorType.NotFound));
}
