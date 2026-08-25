using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;

namespace Full.NET.Modules.Settings.Features.ManageTenantDictTypes;

/// <summary>租户数据字典类型创建、更新与禁用。</summary>
internal sealed partial class TenantDictTypeManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    TenantDictTypeQueryService dictTypeQueries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<DictTypeResponse>> CreateAsync(
        CreateDictTypeRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<DictTypeResponse>> UpdateAsync(
        Guid dictTypeId,
        UpdateDictTypeRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(dictTypeId, request, token),
            cancellationToken);

    public Task<Result<DictTypeResponse>> DisableAsync(
        Guid dictTypeId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(dictTypeId, token),
            cancellationToken);

    /// <summary>
    /// 硬删除已禁用且无活跃字典项的租户字典类型，对应 Admin.NET DeleteDict。
    /// 删除前置校验：类型必须已禁用、无启用字典项；满足后同一事务内级联清理字典项并删除类型本身。
    /// </summary>
    public Task<Result<bool>> DeleteAsync(
        Guid dictTypeId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(dictTypeId, version, token),
            cancellationToken);

    private async Task<Result<DictTypeResponse>> CreateCoreAsync(
        CreateDictTypeRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var code = NormalizeCode(request.Code);
        if (!CodePattern().IsMatch(code))
        {
            return ValidationFailure(
                "Dictionary type code must be 3-64 lowercase letters, numbers, underscores, or hyphens.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            return ValidationFailure("Dictionary type name is invalid.");
        }

        var description = NormalizeOptionalText(request.Description, 512);
        if (description is { Length: > 512 })
        {
            return ValidationFailure("Dictionary type description must not exceed 512 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                TenantDictTypeSql.FindByCode,
                SettingsSqlParameters.Create(("Code", code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return CodeExists();
        }

        var now = clock.UtcNow;
        var dictTypeId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                TenantDictTypeSql.Insert,
                SettingsSqlParameters.Create(
                    ("Id", dictTypeId),
                    ("TenantId", currentTenant.Id),
                    ("Code", code),
                    ("Name", name),
                    ("Description", description),
                    ("DisplayOrder", request.DisplayOrder),
                    ("IsActive", true),
                    ("CreatedAtUtc", now),
                    ("Version", 1)
                ),
                cancellationToken)
            .ConfigureAwait(false);

        return await dictTypeQueries.GetByIdAsync(dictTypeId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DictTypeResponse>> UpdateCoreAsync(
        Guid dictTypeId,
        UpdateDictTypeRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            return ValidationFailure("Dictionary type name is invalid.");
        }

        var description = NormalizeOptionalText(request.Description, 512);
        if (description is { Length: > 512 })
        {
            return ValidationFailure("Dictionary type description must not exceed 512 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                TenantDictTypeSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<DictTypeResponse>();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantDictTypeSql.UpdateTenantDictType,
                SettingsSqlParameters.Create(
                    ("DictTypeId", dictTypeId),
                    ("Name", name),
                    ("Description", description),
                    ("DisplayOrder", request.DisplayOrder),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(dictTypeId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await dictTypeQueries.GetByIdAsync(dictTypeId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DictTypeResponse>> DisableCoreAsync(
        Guid dictTypeId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                TenantDictTypeSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<DictTypeResponse>();
        }

        if (!existing.IsActive)
        {
            return await dictTypeQueries.GetByIdAsync(dictTypeId, cancellationToken)
                .ConfigureAwait(false);
        }

        var activeItemCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantDictTypeSql.CountActiveItems,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (activeItemCount > 0)
        {
            return HasActiveItems<DictTypeResponse>();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantDictTypeSql.DisableTenantDictType,
                SettingsSqlParameters.Create(
                    ("DictTypeId", dictTypeId),
                    ("UpdatedAtUtc", now)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound<DictTypeResponse>();
        }

        return await dictTypeQueries.GetByIdAsync(dictTypeId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DictTypeResponse>> ResolveUpdateFailureAsync(
        Guid dictTypeId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                TenantDictTypeSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<DictTypeResponse>();
        }

        return VersionConflict<DictTypeResponse>();
    }

    /// <summary>
    /// 硬删除核心逻辑：校验租户上下文、已禁用、无活跃字典项后，
    /// 同一事务内级联清理字典项再删除类型。
    /// </summary>
    private async Task<Result<bool>> DeleteCoreAsync(
        Guid dictTypeId,
        int version,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                TenantDictTypeSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<bool>();
        }

        // 类型仍启用时拒绝删除，必须先禁用以避免误删活跃数据。
        if (existing.IsActive)
        {
            return NotDisabled<bool>();
        }

        // 仍有启用字典项时拒绝删除，避免删除被引用的字典类型。
        var activeItemCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantDictTypeSql.CountActiveItems,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (activeItemCount > 0)
        {
            return HasActiveItems<bool>();
        }

        // 先级联清理全部字典项（含已禁用），再删除类型本身。
        await commandExecutor.ExecuteAsync(
                TenantDictTypeSql.DeleteItemsByType,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);

        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantDictTypeSql.DeleteTenantDictType,
                SettingsSqlParameters.Create(
                    ("DictTypeId", dictTypeId),
                    ("Version", version)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            // 删除 0 行表示版本不匹配（存在性已校验），返回版本冲突。
            return VersionConflict<bool>();
        }

        return Result<bool>.Success(true);
    }

    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            throw new TenantContextMissingException("settings.tenant_context_required");
        }
    }

    private static string NormalizeCode(string? code) =>
        code?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private static Result<DictTypeResponse> ValidationFailure(string message) =>
        Result<DictTypeResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.DictTypeNotFound,
            "The dictionary type was not found.",
            ErrorType.NotFound));

    private static Result<DictTypeResponse> CodeExists() =>
        Result<DictTypeResponse>.Failure(new Error(
            SettingsErrorCodes.DictTypeCodeExists,
            "A dictionary type with the same code already exists.",
            ErrorType.Conflict));

    private static Result<T> VersionConflict<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.DictTypeVersionConflict,
            "The dictionary type record was updated concurrently.",
            ErrorType.Conflict));

    private static Result<T> HasActiveItems<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.DictTypeHasActiveItems,
            "The dictionary type still has active items.",
            ErrorType.BusinessRule));

    /// <summary>删除前置校验失败：字典类型仍处于启用状态，必须先禁用。</summary>
    private static Result<T> NotDisabled<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.DictTypeNotDisabled,
            "The dictionary type is still active. Disable it before deleting.",
            ErrorType.BusinessRule));

    [GeneratedRegex(
        "^[a-z][a-z0-9_-]{1,62}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
