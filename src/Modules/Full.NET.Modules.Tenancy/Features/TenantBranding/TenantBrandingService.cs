using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.TenantBranding;

/// <summary>租户品牌文本字段读写；Logo 由 <see cref="TenantBrandingMediaService"/> 单独管理。</summary>
internal sealed class TenantBrandingService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    ICurrentTenant currentTenant,
    TenantCacheInvalidator cacheInvalidator)
{
    /// <summary>Host 作用域按租户标识读取品牌信息。</summary>
    public async Task<Result<TenantBrandingResponse>> GetByTenantIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var record = await LoadByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound<TenantBrandingResponse>()
            : Result<TenantBrandingResponse>.Success(Map(record));
    }

    /// <summary>当前租户上下文读取品牌信息。</summary>
    public Task<Result<TenantBrandingResponse>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.Id is not Guid tenantId)
        {
            return Task.FromResult(NotFound<TenantBrandingResponse>());
        }

        return GetByTenantIdAsync(tenantId, cancellationToken);
    }

    /// <summary>登录壳层读取运行时品牌摘要。</summary>
    public async Task<Result<TenantRuntimeBrandingResponse>> GetRuntimeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable)
        {
            return Result<TenantRuntimeBrandingResponse>.Success(
                new TenantRuntimeBrandingResponse(
                    null,
                    false,
                    null,
                    null,
                    null,
                    null));
        }

        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<TenantBrandingRecord>(
                TenantSql.FindTenantBrandingCurrent,
                TenancySqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound<TenantRuntimeBrandingResponse>();
        }

        return Result<TenantRuntimeBrandingResponse>.Success(MapRuntime(record));
    }

    /// <summary>Host 作用域更新指定租户品牌文本字段。</summary>
    public async Task<Result<TenantBrandingResponse>> UpdateByTenantIdAsync(
        Guid tenantId,
        UpdateTenantBrandingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TenantBrandingPolicy.TryValidateTextFields(
                request.SystemTitle,
                request.ContactPhone,
                request.ContactEmail,
                request.ContactAddress,
                request.Copyright))
        {
            return Invalid<TenantBrandingResponse>();
        }

        var result = await transaction.ExecuteAsync(
                token => UpdateCoreAsync(
                    tenantId,
                    request,
                    TenantSql.UpdateTenantBranding,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess && result.Value is { } branding)
        {
            await InvalidateAfterCommitAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>当前租户上下文更新品牌文本字段。</summary>
    public Task<Result<TenantBrandingResponse>> UpdateCurrentAsync(
        UpdateTenantBrandingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.Id is not Guid tenantId)
        {
            return Task.FromResult(NotFound<TenantBrandingResponse>());
        }

        if (!TenantBrandingPolicy.TryValidateTextFields(
                request.SystemTitle,
                request.ContactPhone,
                request.ContactEmail,
                request.ContactAddress,
                request.Copyright))
        {
            return Task.FromResult(Invalid<TenantBrandingResponse>());
        }

        return UpdateCurrentCoreAsync(tenantId, request, cancellationToken);
    }

    private async Task<Result<TenantBrandingResponse>> UpdateCurrentCoreAsync(
        Guid tenantId,
        UpdateTenantBrandingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await transaction.ExecuteAsync(
                token => UpdateCoreAsync(
                    tenantId,
                    request,
                    TenantSql.UpdateTenantBrandingCurrent,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await InvalidateAfterCommitAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Result<TenantBrandingResponse>> UpdateCoreAsync(
        Guid tenantId,
        UpdateTenantBrandingRequest request,
        SqlStatement updateStatement,
        CancellationToken cancellationToken)
    {
        var existing = await LoadByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<TenantBrandingResponse>();
        }

        var affected = await commandExecutor.ExecuteAsync(
                updateStatement,
                TenancySqlParameters.Create(
                    ("TenantId", tenantId),
                    ("SystemTitle", NormalizeOptional(request.SystemTitle)),
                    ("ContactPhone", NormalizeOptional(request.ContactPhone)),
                    ("ContactEmail", NormalizeOptional(request.ContactEmail)),
                    ("ContactAddress", NormalizeOptional(request.ContactAddress)),
                    ("Copyright", NormalizeOptional(request.Copyright)),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            var stillExists = await LoadByTenantIdAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            return stillExists is null
                ? NotFound<TenantBrandingResponse>()
                : VersionConflict<TenantBrandingResponse>();
        }

        var updated = await LoadByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return updated is null
            ? NotFound<TenantBrandingResponse>()
            : Result<TenantBrandingResponse>.Success(Map(updated));
    }

    private async Task InvalidateAfterCommitAsync(
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

    private Task<TenantBrandingRecord?> LoadByTenantIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<TenantBrandingRecord>(
            TenantSql.FindTenantBrandingById,
            TenancySqlParameters.Create(("TenantId", tenantId)),
            cancellationToken);

    private static TenantBrandingResponse Map(TenantBrandingRecord record) =>
        new(
            record.TenantId,
            record.SystemTitle,
            record.LogoFileId,
            record.ContactPhone,
            record.ContactEmail,
            record.ContactAddress,
            record.Copyright,
            record.Version);

    private static TenantRuntimeBrandingResponse MapRuntime(TenantBrandingRecord record) =>
        new(
            record.SystemTitle,
            record.LogoFileId is not null,
            record.ContactPhone,
            record.ContactEmail,
            record.ContactAddress,
            record.Copyright);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            TenancyErrorCodes.NotFound,
            "The tenant was not found.",
            ErrorType.NotFound));

    private static Result<T> VersionConflict<T>() =>
        Result<T>.Failure(new Error(
            TenancyErrorCodes.VersionConflict,
            "The tenant record was updated concurrently.",
            ErrorType.Conflict));

    private static Result<T> Invalid<T>() =>
        Result<T>.Failure(new Error(
            TenancyErrorCodes.BrandingInvalid,
            "The tenant branding fields are invalid.",
            ErrorType.Validation));
}
