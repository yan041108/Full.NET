using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;

namespace Full.NET.Modules.Settings.Features.ManageHostConfigEntries;

/// <summary>Host 系统配置项创建、更新与禁用。</summary>
internal sealed partial class HostConfigEntryManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostConfigEntryQueryService configEntryQueries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<ConfigEntryResponse>> CreateAsync(
        CreateConfigEntryRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<ConfigEntryResponse>> UpdateAsync(
        Guid configEntryId,
        UpdateConfigEntryRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(configEntryId, request, token),
            cancellationToken);

    public Task<Result<ConfigEntryResponse>> DisableAsync(
        Guid configEntryId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(configEntryId, token),
            cancellationToken);

    /// <summary>
    /// 硬删除已禁用的配置项，对应 Admin.NET DeleteConfig。
    /// 删除前置校验：配置项必须已禁用；通过后直接硬删除。
    /// </summary>
    public Task<Result<bool>> DeleteAsync(
        Guid configEntryId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(configEntryId, version, token),
            cancellationToken);

    /// <summary>
    /// 批量硬删除已禁用的配置项，对应 Admin.NET batchDeleteConfig。
    /// 逐条校验已禁用后批量删除，任一项未禁用则整体拒绝。
    /// </summary>
    public Task<Result<bool>> BatchDeleteAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => BatchDeleteCoreAsync(ids, token),
            cancellationToken);

    /// <summary>
    /// 批量更新配置项值，对应 Admin.NET batchUpdateConfigValue。
    /// 按 ConfigKey 定位，校验值类型后逐条更新，任一失败则整体回滚。
    /// </summary>
    public Task<Result<bool>> BatchUpdateValuesAsync(
        IReadOnlyCollection<ConfigValueUpdate> updates,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => BatchUpdateValuesCoreAsync(updates, token),
            cancellationToken);

    private async Task<Result<ConfigEntryResponse>> CreateCoreAsync(
        CreateConfigEntryRequest request,
        CancellationToken cancellationToken)
    {
        var configKey = NormalizeConfigKey(request.ConfigKey);
        if (!ConfigKeyPattern().IsMatch(configKey))
        {
            return ValidationFailure(
                "Configuration key must be 3-128 lowercase letters, numbers, dots, underscores, or hyphens.");
        }

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > 128)
        {
            return ValidationFailure("Configuration display name is invalid.");
        }

        var description = NormalizeOptionalText(request.Description, 512);
        if (description is { Length: > 512 })
        {
            return ValidationFailure("Configuration description must not exceed 512 characters.");
        }

        var valueKind = request.ValueKind?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ConfigValueKinds.All.Contains(valueKind, StringComparer.Ordinal))
        {
            return ValidationFailure(
                "Configuration value kind must be string, boolean, integer, decimal, or json.");
        }

        if (!TryNormalizeValue(valueKind, request.Value, out var normalizedValue, out var valueError))
        {
            return ValidationFailure(valueError);
        }

        var groupName = NormalizeGroupName(request.GroupName);
        if (groupName is { Length: > 64 })
        {
            return ValidationFailure("Configuration group name must not exceed 64 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                ConfigEntrySql.FindIdentityByKey,
                SettingsSqlParameters.Create(("ConfigKey", configKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return KeyExists();
        }

        var now = clock.UtcNow;
        var configEntryId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                ConfigEntrySql.Insert,
                SettingsSqlParameters.Create(
                    ("Id", configEntryId),
                    ("ConfigKey", configKey),
                    ("DisplayName", displayName),
                    ("Description", description),
                    ("GroupName", groupName),
                    ("ValueKind", valueKind),
                    ("Value", normalizedValue),
                    ("DisplayOrder", request.DisplayOrder),
                    ("IsActive", true),
                    ("CreatedAtUtc", now),
                    ("Version", 1)
                ),
                cancellationToken)
            .ConfigureAwait(false);

        return await configEntryQueries.GetByIdAsync(configEntryId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ConfigEntryResponse>> UpdateCoreAsync(
        Guid configEntryId,
        UpdateConfigEntryRequest request,
        CancellationToken cancellationToken)
    {
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > 128)
        {
            return ValidationFailure("Configuration display name is invalid.");
        }

        var description = NormalizeOptionalText(request.Description, 512);
        if (description is { Length: > 512 })
        {
            return ValidationFailure("Configuration description must not exceed 512 characters.");
        }

        var groupName = NormalizeGroupName(request.GroupName);
        if (groupName is { Length: > 64 })
        {
            return ValidationFailure("Configuration group name must not exceed 64 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                ConfigEntrySql.FindIdentityById,
                SettingsSqlParameters.Create(("ConfigEntryId", configEntryId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<ConfigEntryResponse>();
        }

        if (!TryNormalizeValue(
                existing.ValueKind,
                request.Value,
                out var normalizedValue,
                out var valueError))
        {
            return ValidationFailure(valueError);
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                ConfigEntrySql.UpdateHostConfigEntry,
                SettingsSqlParameters.Create(
                    ("ConfigEntryId", configEntryId),
                    ("DisplayName", displayName),
                    ("Description", description),
                    ("GroupName", groupName),
                    ("Value", normalizedValue),
                    ("DisplayOrder", request.DisplayOrder),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(configEntryId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await configEntryQueries.GetByIdAsync(configEntryId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ConfigEntryResponse>> DisableCoreAsync(
        Guid configEntryId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                ConfigEntrySql.FindIdentityById,
                SettingsSqlParameters.Create(("ConfigEntryId", configEntryId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<ConfigEntryResponse>();
        }

        if (!existing.IsActive)
        {
            return await configEntryQueries.GetByIdAsync(configEntryId, cancellationToken)
                .ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                ConfigEntrySql.DisableHostConfigEntry,
                SettingsSqlParameters.Create(
                    ("ConfigEntryId", configEntryId),
                    ("UpdatedAtUtc", now)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound<ConfigEntryResponse>();
        }

        return await configEntryQueries.GetByIdAsync(configEntryId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ConfigEntryResponse>> ResolveUpdateFailureAsync(
        Guid configEntryId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                ConfigEntrySql.FindIdentityById,
                SettingsSqlParameters.Create(("ConfigEntryId", configEntryId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<ConfigEntryResponse>();
        }

        return VersionConflict<ConfigEntryResponse>();
    }

    /// <summary>
    /// 硬删除核心逻辑：校验配置项已禁用后直接硬删除，WHERE 同时校验 Version 防并发。
    /// </summary>
    private async Task<Result<bool>> DeleteCoreAsync(
        Guid configEntryId,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                ConfigEntrySql.FindIdentityById,
                SettingsSqlParameters.Create(("ConfigEntryId", configEntryId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<bool>();
        }

        // 配置项仍启用时拒绝删除，必须先禁用以避免误删活跃数据。
        if (existing.IsActive)
        {
            return NotDisabled<bool>();
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ConfigEntrySql.DeleteConfigEntry,
                SettingsSqlParameters.Create(
                    ("ConfigEntryId", configEntryId),
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

    /// <summary>
    /// 批量硬删除核心逻辑：逐条校验已禁用后批量删除，任一项未禁用或不存在则整体拒绝。
    /// </summary>
    private async Task<Result<bool>> BatchDeleteCoreAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids is null || ids.Count == 0)
        {
            return Result<bool>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Configuration entry ids must not be empty.",
                ErrorType.Validation));
        }

        // 逐条校验已禁用，任一项未禁用或不存在则整体拒绝，保证批量删除的原子性语义。
        foreach (var id in ids)
        {
            var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                    ConfigEntrySql.FindIdentityById,
                    SettingsSqlParameters.Create(("ConfigEntryId", id)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return NotFound<bool>();
            }

            if (existing.IsActive)
            {
                return NotDisabled<bool>();
            }
        }

        await commandExecutor.ExecuteAsync(
                ConfigEntrySql.BatchDeleteConfigEntries,
                SettingsSqlParameters.Create(("Ids", ids)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// 批量更新值核心逻辑：按 ConfigKey 逐条定位、校验值类型后更新，任一失败则整体回滚。
    /// </summary>
    private async Task<Result<bool>> BatchUpdateValuesCoreAsync(
        IReadOnlyCollection<ConfigValueUpdate> updates,
        CancellationToken cancellationToken)
    {
        if (updates is null || updates.Count == 0)
        {
            return Result<bool>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Configuration value updates must not be empty.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        foreach (var update in updates)
        {
            var configKey = NormalizeConfigKey(update.ConfigKey);
            if (configKey.Length == 0)
            {
                return Result<bool>.Failure(new Error(
                    ValidationErrorCodes.Failed,
                    "Configuration key must not be empty.",
                    ErrorType.Validation));
            }

            var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                    ConfigEntrySql.FindIdentityByKey,
                    SettingsSqlParameters.Create(("ConfigKey", configKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return NotFound<bool>();
            }

            if (!TryNormalizeValue(existing.ValueKind, update.Value, out var normalizedValue, out var valueError))
            {
                return Result<bool>.Failure(new Error(
                    ValidationErrorCodes.Failed,
                    valueError,
                    ErrorType.Validation));
            }

            await commandExecutor.ExecuteAsync(
                    ConfigEntrySql.UpdateValueByConfigKey,
                    SettingsSqlParameters.Create(
                        ("ConfigKey", configKey),
                        ("Value", normalizedValue),
                        ("UpdatedAtUtc", now)
                    ),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<bool>.Success(true);
    }

    private static bool TryNormalizeValue(
        string valueKind,
        string? rawValue,
        out string normalizedValue,
        out string errorMessage)
    {
        normalizedValue = string.Empty;
        errorMessage = string.Empty;
        var value = rawValue?.Trim() ?? string.Empty;
        if (value.Length > 4000)
        {
            errorMessage = "Configuration value must not exceed 4000 characters.";
            return false;
        }

        switch (valueKind)
        {
            case ConfigValueKinds.String:
                normalizedValue = value;
                return true;
            case ConfigValueKinds.Boolean:
                if (bool.TryParse(value, out var booleanValue))
                {
                    normalizedValue = booleanValue ? "true" : "false";
                    return true;
                }

                errorMessage = "Boolean configuration value must be true or false.";
                return false;
            case ConfigValueKinds.Integer:
                if (long.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var integerValue))
                {
                    normalizedValue = integerValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                errorMessage = "Integer configuration value is invalid.";
                return false;
            case ConfigValueKinds.Decimal:
                if (decimal.TryParse(
                        value,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var decimalValue))
                {
                    normalizedValue = decimalValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                errorMessage = "Decimal configuration value is invalid.";
                return false;
            case ConfigValueKinds.Json:
                if (value.Length == 0)
                {
                    errorMessage = "JSON configuration value must not be empty.";
                    return false;
                }

                try
                {
                    using var document = JsonDocument.Parse(value);
                    normalizedValue = value;
                    return true;
                }
                catch (JsonException)
                {
                    errorMessage = "JSON configuration value is invalid.";
                    return false;
                }
            case ConfigValueKinds.Secret:
                normalizedValue = value;
                return true;
            default:
                errorMessage = "Configuration value kind is unsupported.";
                return false;
        }
    }

    private static string NormalizeConfigKey(string? configKey) =>
        configKey?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    /// <summary>
    /// 归一化配置项分组名：去空白，空字符串视为未分组（null），对应 Admin.NET SysConfig.GroupName。
    /// </summary>
    private static string? NormalizeGroupName(string? groupName)
    {
        var normalized = groupName?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<ConfigEntryResponse> ValidationFailure(string message) =>
        Result<ConfigEntryResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.ConfigEntryNotFound,
            "The system configuration entry was not found.",
            ErrorType.NotFound));

    private static Result<ConfigEntryResponse> KeyExists() =>
        Result<ConfigEntryResponse>.Failure(new Error(
            SettingsErrorCodes.ConfigEntryKeyExists,
            "A system configuration entry with the same key already exists.",
            ErrorType.Conflict));

    private static Result<T> VersionConflict<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.ConfigEntryVersionConflict,
            "The system configuration entry was updated concurrently.",
            ErrorType.Conflict));

    /// <summary>删除前置校验失败：配置项仍处于启用状态，必须先禁用。</summary>
    private static Result<T> NotDisabled<T>() =>
        Result<T>.Failure(new Error(
            SettingsErrorCodes.ConfigEntryNotDisabled,
            "The system configuration entry is still active. Disable it before deleting.",
            ErrorType.BusinessRule));

    [GeneratedRegex(
        "^[a-z][a-z0-9._-]{1,126}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ConfigKeyPattern();
}
