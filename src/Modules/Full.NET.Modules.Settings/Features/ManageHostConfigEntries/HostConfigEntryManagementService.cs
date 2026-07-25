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

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                ConfigEntrySql.FindIdentityByKey,
                new { ConfigKey = configKey },
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
                new
                {
                    Id = configEntryId,
                    ConfigKey = configKey,
                    DisplayName = displayName,
                    Description = description,
                    ValueKind = valueKind,
                    Value = normalizedValue,
                    DisplayOrder = request.DisplayOrder,
                    IsActive = true,
                    CreatedAtUtc = now,
                    Version = 1,
                },
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

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryIdentityRecord>(
                ConfigEntrySql.FindIdentityById,
                new { ConfigEntryId = configEntryId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
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
                new
                {
                    ConfigEntryId = configEntryId,
                    DisplayName = displayName,
                    Description = description,
                    Value = normalizedValue,
                    DisplayOrder = request.DisplayOrder,
                    UpdatedAtUtc = now,
                    request.Version,
                },
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
                new { ConfigEntryId = configEntryId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (!existing.IsActive)
        {
            return await configEntryQueries.GetByIdAsync(configEntryId, cancellationToken)
                .ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                ConfigEntrySql.DisableHostConfigEntry,
                new
                {
                    ConfigEntryId = configEntryId,
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
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
                new { ConfigEntryId = configEntryId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        return VersionConflict();
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

    private static Result<ConfigEntryResponse> ValidationFailure(string message) =>
        Result<ConfigEntryResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<ConfigEntryResponse> NotFound() =>
        Result<ConfigEntryResponse>.Failure(new Error(
            SettingsErrorCodes.ConfigEntryNotFound,
            "The system configuration entry was not found.",
            ErrorType.NotFound));

    private static Result<ConfigEntryResponse> KeyExists() =>
        Result<ConfigEntryResponse>.Failure(new Error(
            SettingsErrorCodes.ConfigEntryKeyExists,
            "A system configuration entry with the same key already exists.",
            ErrorType.Conflict));

    private static Result<ConfigEntryResponse> VersionConflict() =>
        Result<ConfigEntryResponse>.Failure(new Error(
            SettingsErrorCodes.ConfigEntryVersionConflict,
            "The system configuration entry was updated concurrently.",
            ErrorType.Conflict));

    [GeneratedRegex(
        "^[a-z][a-z0-9._-]{1,126}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ConfigKeyPattern();
}
