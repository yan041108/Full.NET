using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageTenantDictTypes;
using Full.NET.Modules.Settings.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Settings.Features.ManageTenantDictItems;

/// <summary>租户数据字典项创建、更新与禁用。</summary>
internal sealed partial class TenantDictItemManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    TenantDictItemQueryService dictItemQueries,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<DictItemResponse>> CreateAsync(
        Guid dictTypeId,
        CreateDictItemRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(dictTypeId, request, token),
            cancellationToken);

    public Task<Result<DictItemResponse>> UpdateAsync(
        Guid dictItemId,
        UpdateDictItemRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(dictItemId, request, token),
            cancellationToken);

    public Task<Result<DictItemResponse>> DisableAsync(
        Guid dictItemId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(dictItemId, token),
            cancellationToken);

    /// <summary>
    /// 硬删除已禁用的租户字典项，对应 Admin.NET DeleteDictItem。
    /// 删除前置校验：字典项必须已禁用；通过后直接硬删除。
    /// </summary>
    public Task<Result<bool>> DeleteAsync(
        Guid dictItemId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(dictItemId, version, token),
            cancellationToken);

    private async Task<Result<DictItemResponse>> CreateCoreAsync(
        Guid dictTypeId,
        CreateDictItemRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var typeExists = await queryExecutor.QuerySingleOrDefaultAsync<DictTypeIdentityRecord>(
                TenantDictTypeSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictTypeId", dictTypeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (typeExists is null)
        {
            return TypeNotFound();
        }

        var label = request.Label?.Trim() ?? string.Empty;
        if (label.Length is < 1 or > 128)
        {
            return ValidationFailure("Dictionary item label is invalid.");
        }

        var value = NormalizeValue(request.Value);
        if (!ValuePattern().IsMatch(value))
        {
            return ValidationFailure(
                "Dictionary item value must be 2-128 lowercase letters, numbers, underscores, or hyphens.");
        }

        var color = NormalizeOptionalText(request.Color, 32);
        if (color is { Length: > 32 })
        {
            return ValidationFailure("Dictionary item color must not exceed 32 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictItemIdentityRecord>(
                TenantDictItemSql.FindByTypeAndValue,
                SettingsSqlParameters.Create(
                    ("DictTypeId", dictTypeId),
                    ("Value", value)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return ValueExists();
        }

        var now = clock.UtcNow;
        var dictItemId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                TenantDictItemSql.Insert,
                SettingsSqlParameters.Create(
                    ("Id", dictItemId),
                    ("DictTypeId", dictTypeId),
                    ("Label", label),
                    ("Value", value),
                    ("Color", color),
                    ("DisplayOrder", request.DisplayOrder),
                    ("IsActive", true),
                    ("CreatedAtUtc", now),
                    ("Version", 1)
                ),
                cancellationToken)
            .ConfigureAwait(false);

        return await dictItemQueries.GetByIdAsync(dictItemId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DictItemResponse>> UpdateCoreAsync(
        Guid dictItemId,
        UpdateDictItemRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var label = request.Label?.Trim() ?? string.Empty;
        if (label.Length is < 1 or > 128)
        {
            return ValidationFailure("Dictionary item label is invalid.");
        }

        var color = NormalizeOptionalText(request.Color, 32);
        if (color is { Length: > 32 })
        {
            return ValidationFailure("Dictionary item color must not exceed 32 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictItemIdentityRecord>(
                TenantDictItemSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictItemId", dictItemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<DictItemResponse>();
        }

        var now = clock.UtcNow;
        var updateStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => TenantDictItemSql.UpdateTenantDictItem,
            DatabaseProvider.MySql => TenantDictItemSql.UpdateTenantDictItemMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var affectedRows = await commandExecutor.ExecuteAsync(
                updateStatement,
                SettingsSqlParameters.Create(
                    ("DictItemId", dictItemId),
                    ("Label", label),
                    ("Color", color),
                    ("DisplayOrder", request.DisplayOrder),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(dictItemId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await dictItemQueries.GetByIdAsync(dictItemId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DictItemResponse>> DisableCoreAsync(
        Guid dictItemId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictItemIdentityRecord>(
                TenantDictItemSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictItemId", dictItemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<DictItemResponse>();
        }

        if (!existing.IsActive)
        {
            return await dictItemQueries.GetByIdAsync(dictItemId, cancellationToken)
                .ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var disableStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => TenantDictItemSql.DisableTenantDictItem,
            DatabaseProvider.MySql => TenantDictItemSql.DisableTenantDictItemMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var affectedRows = await commandExecutor.ExecuteAsync(
                disableStatement,
                SettingsSqlParameters.Create(
                    ("DictItemId", dictItemId),
                    ("UpdatedAtUtc", now)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound<DictItemResponse>();
        }

        return await dictItemQueries.GetByIdAsync(dictItemId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<DictItemResponse>> ResolveUpdateFailureAsync(
        Guid dictItemId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictItemIdentityRecord>(
                TenantDictItemSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictItemId", dictItemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<DictItemResponse>();
        }

        return VersionConflict<DictItemResponse>();
    }

    /// <summary>
    /// 硬删除核心逻辑：校验租户上下文与字典项已禁用后直接硬删除，
    /// WHERE 同时校验 Version 防并发。
    /// </summary>
    private async Task<Result<bool>> DeleteCoreAsync(
        Guid dictItemId,
        int version,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DictItemIdentityRecord>(
                TenantDictItemSql.FindIdentityById,
                SettingsSqlParameters.Create(("DictItemId", dictItemId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<bool>();
        }

        // 字典项仍启用时拒绝删除，必须先禁用以避免误删活跃数据。
        if (existing.IsActive)
        {
            return NotDisabled<bool>();
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantDictItemSql.DeleteTenantDictItem,
                SettingsSqlParameters.Create(
                    ("DictItemId", dictItemId),
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

    private static string NormalizeValue(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private static Result<DictItemResponse> ValidationFailure(string message) =>
        Result<DictItemResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<DictItemResponse> TypeNotFound() =>
        Result<DictItemResponse>.Failure(new Error(
            SettingsErrorCodes.DictTypeNotFound,
            "The dictionary type was not found.",
            ErrorType.NotFound));

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.DictItemNotFound,
            "The dictionary item was not found.",
            ErrorType.NotFound));

    private static Result<DictItemResponse> ValueExists() =>
        Result<DictItemResponse>.Failure(new Error(
            SettingsErrorCodes.DictItemValueExists,
            "A dictionary item with the same value already exists in this type.",
            ErrorType.Conflict));

    private static Result<T> VersionConflict<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.DictItemVersionConflict,
            "The dictionary item record was updated concurrently.",
            ErrorType.Conflict));

    /// <summary>删除前置校验失败：字典项仍处于启用状态，必须先禁用。</summary>
    private static Result<T> NotDisabled<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.DictItemNotDisabled,
            "The dictionary item is still active. Disable it before deleting.",
            ErrorType.BusinessRule));

    [GeneratedRegex(
        "^[a-z][a-z0-9_-]{0,126}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ValuePattern();
}
