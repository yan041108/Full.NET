using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageRegistrationWays;

/// <summary>注册方式创建、更新与删除。</summary>
internal sealed class RegistrationWayManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    RegistrationWayQueryService queries,
    IIdentityActiveTenantDirectory activeTenants,
    IIdentityOrganizationUnitDirectory organizationUnits,
    IIdentityOrganizationPositionDirectory positions,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly Regex CodePattern = new(
        "^[a-z][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>名称允许的最大字符数。</summary>
    internal const int MaxNameLength = 128;

    /// <summary>备注允许的最大字符数。</summary>
    internal const int MaxRemarkLength = 256;

    /// <summary>创建注册方式。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建结果或稳定业务错误。</returns>
    public Task<Result<RegistrationWayResponse>> CreateAsync(
        CreateRegistrationWayRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    /// <summary>更新注册方式。</summary>
    /// <param name="wayId">注册方式标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的注册方式或稳定业务错误。</returns>
    public Task<Result<RegistrationWayResponse>> UpdateAsync(
        Guid wayId,
        UpdateRegistrationWayRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(wayId, request, token),
            cancellationToken);

    /// <summary>删除注册方式。</summary>
    /// <param name="wayId">注册方式标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除成功或稳定业务错误。</returns>
    public Task<Result<bool>> DeleteAsync(
        Guid wayId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(wayId, token),
            cancellationToken);

    private async Task<Result<RegistrationWayResponse>> CreateCoreAsync(
        CreateRegistrationWayRequest request,
        CancellationToken cancellationToken)
    {
        var metadataValidation = ValidateMetadata(request.Name, request.Code, request.Remark);
        if (!metadataValidation.IsSuccess)
        {
            return Result<RegistrationWayResponse>.Failure(metadataValidation.Error!);
        }

        var referencesValidation = await ValidateReferencesAsync(
                request.TenantId,
                request.RoleId,
                request.OrganizationUnitId,
                request.PositionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!referencesValidation.IsSuccess)
        {
            return Result<RegistrationWayResponse>.Failure(referencesValidation.Error!);
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<RegistrationWayRecord>(
                RegistrationWaySql.FindByTenantAndCode,
                IdentitySqlParameters.Create(
                    ("TenantId", request.TenantId),
                    ("Code", metadataValidation.Value!.Code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<RegistrationWayResponse>.Failure(new Error(
                IdentityErrorCodes.RegistrationWayCodeExists,
                "The registration way code already exists for this tenant.",
                ErrorType.Conflict));
        }

        var now = clock.UtcNow;
        var wayId = idGenerator.NewId();
        var record = new RegistrationWayRecord
        {
            Id = wayId,
            TenantId = request.TenantId,
            Name = metadataValidation.Value!.Name,
            Code = metadataValidation.Value.Code,
            IsEnabled = request.IsEnabled,
            RoleId = request.RoleId,
            OrganizationUnitId = request.OrganizationUnitId,
            PositionId = request.PositionId,
            SortOrder = request.SortOrder,
            Remark = metadataValidation.Value.Remark,
            CreatedAtUtc = now,
            UpdatedAtUtc = null,
            Version = 1,
        };
        await commandExecutor.ExecuteAsync(
                RegistrationWaySql.Insert,
                record,
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(wayId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<RegistrationWayResponse>> UpdateCoreAsync(
        Guid wayId,
        UpdateRegistrationWayRequest request,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<RegistrationWayRecord>(
                RegistrationWaySql.FindById,
                IdentitySqlParameters.Create(("WayId", wayId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFound();
        }

        var metadataValidation = ValidateMetadata(request.Name, request.Code, request.Remark);
        if (!metadataValidation.IsSuccess)
        {
            return Result<RegistrationWayResponse>.Failure(metadataValidation.Error!);
        }

        var referencesValidation = await ValidateReferencesAsync(
                current.TenantId,
                request.RoleId,
                request.OrganizationUnitId,
                request.PositionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!referencesValidation.IsSuccess)
        {
            return Result<RegistrationWayResponse>.Failure(referencesValidation.Error!);
        }

        if (!string.Equals(
                metadataValidation.Value!.Code,
                current.Code,
                StringComparison.Ordinal))
        {
            var existing = await queryExecutor.QuerySingleOrDefaultAsync<RegistrationWayRecord>(
                    RegistrationWaySql.FindByTenantAndCode,
                    IdentitySqlParameters.Create(
                        ("TenantId", current.TenantId),
                        ("Code", metadataValidation.Value.Code)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null && existing.Id != wayId)
            {
                return Result<RegistrationWayResponse>.Failure(new Error(
                    IdentityErrorCodes.RegistrationWayCodeExists,
                    "The registration way code already exists for this tenant.",
                    ErrorType.Conflict));
            }
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                RegistrationWaySql.Update,
                IdentitySqlParameters.Create(
                    ("WayId", wayId),
                    ("Name", metadataValidation.Value!.Name),
                    ("Code", metadataValidation.Value.Code),
                    ("IsEnabled", request.IsEnabled),
                    ("RoleId", request.RoleId),
                    ("OrganizationUnitId", request.OrganizationUnitId),
                    ("PositionId", request.PositionId),
                    ("SortOrder", request.SortOrder),
                    ("Remark", metadataValidation.Value.Remark),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(wayId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid wayId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<RegistrationWayRecord>(
                RegistrationWaySql.FindById,
                IdentitySqlParameters.Create(("WayId", wayId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.RegistrationWayNotFound,
                "The registration way was not found.",
                ErrorType.NotFound));
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                RegistrationWaySql.Delete,
                IdentitySqlParameters.Create(("WayId", wayId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.RegistrationWayNotFound,
                "The registration way was not found.",
                ErrorType.NotFound));
        }

        return Result<bool>.Success(true);
    }

    private async Task<Result<bool>> ValidateReferencesAsync(
        Guid tenantId,
        Guid roleId,
        Guid organizationUnitId,
        Guid? positionId,
        CancellationToken cancellationToken)
    {
        if (!await activeTenants.IsActiveTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.RegistrationWayTenantInactive,
                "The target tenant was not found or is inactive.",
                ErrorType.Validation));
        }

        var role = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                RegistrationWaySql.FindActiveTenantRole,
                IdentitySqlParameters.Create(
                    ("RoleId", roleId),
                    ("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (role is null)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.RegistrationWayRoleNotFound,
                "The target role was not found, is inactive, or belongs to another tenant.",
                ErrorType.Validation));
        }

        var unit = await organizationUnits.FindActiveUnitAsync(
                tenantId,
                organizationUnitId,
                cancellationToken)
            .ConfigureAwait(false);
        if (unit is null)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.RegistrationWayOrganizationUnitNotFound,
                "The target organization unit was not found, is inactive, or belongs to another tenant.",
                ErrorType.Validation));
        }

        if (positionId.HasValue)
        {
            var position = await positions.FindActivePositionAsync(
                    tenantId,
                    positionId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            if (position is null)
            {
                return Result<bool>.Failure(new Error(
                    IdentityErrorCodes.RegistrationWayPositionNotFound,
                    "The target position was not found, is inactive, or belongs to another tenant.",
                    ErrorType.Validation));
            }
        }

        return Result<bool>.Success(true);
    }

    internal static Result<ValidatedMetadata> ValidateMetadata(
        string? name,
        string? code,
        string? remark)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > MaxNameLength)
        {
            return Result<ValidatedMetadata>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Registration way name is invalid.",
                ErrorType.Validation));
        }

        var normalizedCode = code?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!CodePattern.IsMatch(normalizedCode))
        {
            return Result<ValidatedMetadata>.Failure(new Error(
                IdentityErrorCodes.RegistrationWayInvalidCode,
                "Registration way code is invalid.",
                ErrorType.Validation));
        }

        var normalizedRemark = NormalizeOptional(remark, MaxRemarkLength);
        if (!normalizedRemark.IsSuccess)
        {
            return Result<ValidatedMetadata>.Failure(normalizedRemark.Error!);
        }

        return Result<ValidatedMetadata>.Success(new ValidatedMetadata(
            normalizedName,
            normalizedCode,
            normalizedRemark.Value));
    }

    private static Result<string?> NormalizeOptional(string? value, int maxLength)
    {
        if (value is null)
        {
            return Result<string?>.Success(null);
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return Result<string?>.Success(null);
        }

        if (normalized.Length > maxLength)
        {
            return Result<string?>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Field length exceeds the allowed limit.",
                ErrorType.Validation));
        }

        return Result<string?>.Success(normalized);
    }

    private static Result<RegistrationWayResponse> NotFound() =>
        Result<RegistrationWayResponse>.Failure(new Error(
            IdentityErrorCodes.RegistrationWayNotFound,
            "The registration way was not found.",
            ErrorType.NotFound));

    private static Result<RegistrationWayResponse> VersionConflict() =>
        Result<RegistrationWayResponse>.Failure(new Error(
            IdentityErrorCodes.RegistrationWayVersionConflict,
            "The registration way was modified by another request.",
            ErrorType.Conflict));

    internal sealed record ValidatedMetadata(
        string Name,
        string Code,
        string? Remark);
}
