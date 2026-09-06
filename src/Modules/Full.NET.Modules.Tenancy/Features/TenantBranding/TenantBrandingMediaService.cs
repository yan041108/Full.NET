using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
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
            fileName,
            contentType,
            content,
            contentLength,
            cancellationToken);

    /// <summary>Host 作用域删除租户 Logo。</summary>
    public Task<Result<TenantBrandingResponse>> DeleteLogoByTenantIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        DeleteLogoAsync(tenantId, TenantSql.ClearTenantLogo, cancellationToken);

    /// <summary>当前租户上下文删除 Logo。</summary>
    public Task<Result<TenantBrandingResponse>> DeleteLogoCurrentAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        DeleteLogoAsync(tenantId, TenantSql.ClearTenantLogoCurrent, cancellationToken);

    /// <summary>按租户标识打开 Logo 内容流。</summary>
    public Task<Result<HostFileContent>> OpenLogoContentAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        OpenContentAsync(tenantId, cancellationToken);

    private async Task<Result<TenantBrandingResponse>> UploadLogoAsync(
        Guid tenantId,
        Guid uploadedByUserId,
        SqlStatement updateStatement,
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

        var branding = await queryExecutor
            .QuerySingleOrDefaultAsync<TenantBrandingRecord>(
                TenantSql.FindTenantBrandingById,
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (branding is null)
        {
            return NotFound<TenantBrandingResponse>();
        }

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
            return Result<TenantBrandingResponse>.Failure(uploadResult.Error!);
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
            return LogoInvalid<TenantBrandingResponse>();
        }

        var previousFileId = branding.LogoFileId;
        var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(tenantId, fileId);
        var claimResult = await hostFileReferenceClaimService
            .ClaimAsync(
                new HostFileReferenceClaimRequest(
                    idempotencyKey,
                    HostFileReferenceClaimConsumerModules.Tenancy,
                    tenantId,
                    fileId),
                cancellationToken)
            .ConfigureAwait(false);
        if (!claimResult.IsSuccess)
        {
            return Result<TenantBrandingResponse>.Failure(claimResult.Error!);
        }

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
            await hostFileReferenceClaimService
                .ReleaseAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            return Result<TenantBrandingResponse>.Failure(bindResult.Error!);
        }

        var confirmResult = await hostFileReferenceClaimService
            .ConfirmAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (!confirmResult.IsSuccess)
        {
            return Result<TenantBrandingResponse>.Failure(confirmResult.Error!);
        }

        if (previousFileId is Guid oldFileId && oldFileId != fileId)
        {
            await ReleaseLogoClaimAsync(tenantId, oldFileId, cancellationToken)
                .ConfigureAwait(false);
        }

        await InvalidateBrandingCacheAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return await brandingService.GetByTenantIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantBrandingResponse>> DeleteLogoAsync(
        Guid tenantId,
        SqlStatement clearStatement,
        CancellationToken cancellationToken)
    {
        var branding = await queryExecutor
            .QuerySingleOrDefaultAsync<TenantBrandingRecord>(
                TenantSql.FindTenantBrandingById,
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (branding is null)
        {
            return NotFound<TenantBrandingResponse>();
        }

        if (branding.LogoFileId is not Guid fileId)
        {
            return await brandingService.GetByTenantIdAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
        }

        _ = await commandExecutor.ExecuteAsync(
                clearStatement,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("UpdatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        await ReleaseLogoClaimAsync(tenantId, fileId, cancellationToken).ConfigureAwait(false);
        await InvalidateBrandingCacheAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return await brandingService.GetByTenantIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostFileContent>> OpenContentAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var branding = await queryExecutor
            .QuerySingleOrDefaultAsync<TenantBrandingRecord>(
                TenantSql.FindTenantBrandingById,
                TenancySqlParameters.Create(("TenantId", tenantId)),
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

        var content = await hostFileContentReader
            .OpenReadyContentAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
        return content.IsSuccess
            ? content
            : LogoNotFound<HostFileContent>();
    }

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

    private async Task ReleaseLogoClaimAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(tenantId, fileId);
        _ = await hostFileReferenceClaimService
            .ReleaseAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
    }

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
