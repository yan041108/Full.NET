using Full.NET.Abstractions.Results;
using Full.NET.Modules.Settings.Catalogs;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Settings.Features.ManageMyGridPreferences;

/// <summary>集中验证并规范化用户可控的列展示数据。</summary>
internal static class GridPreferencePolicy
{
    private const int MinimumWidth = 48;
    private const int MaximumWidth = 2000;

    public static Result<IReadOnlyList<GridColumnPreference>> ValidateAndNormalize(
        GridPreferenceDefinition definition,
        UpdateGridPreferenceRequest request)
    {
        if (request.SchemaVersion != definition.SchemaVersion)
        {
            return Failure(
                SettingsErrorCodes.GridSchemaVersionMismatch,
                "The Grid schema version no longer matches the local catalog.",
                ErrorType.Conflict);
        }

        if (request.Version < 0
            || request.Columns is null
            || request.Columns.Count > definition.ColumnKeys.Count)
        {
            return InvalidPreference();
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        foreach (var column in request.Columns)
        {
            if (column is null)
            {
                return InvalidPreference();
            }

            if (!definition.ColumnKeys.Contains(column.ColumnKey))
            {
                return Failure(
                    SettingsErrorCodes.GridColumnUnknown,
                    "The Grid column key is not published by the local catalog.",
                    ErrorType.Validation);
            }

            if (!keys.Add(column.ColumnKey))
            {
                return Failure(
                    SettingsErrorCodes.GridColumnDuplicate,
                    "A Grid column key can appear only once.",
                    ErrorType.Validation);
            }

            if (column.Order < 0 || !orders.Add(column.Order))
            {
                return InvalidPreference();
            }

            if (column.Width is { } width
                && width is < MinimumWidth or > MaximumWidth)
            {
                return InvalidPreference();
            }

            if (column.Fixed is not null
                && column.Fixed is not ("left" or "right"))
            {
                return InvalidPreference();
            }
        }

        return Result<IReadOnlyList<GridColumnPreference>>.Success(
            request.Columns
                .OrderBy(column => column.Order)
                .ThenBy(column => column.ColumnKey, StringComparer.Ordinal)
                .ToArray());
    }

    public static GridPreferenceResponse Restore(
        GridPreferenceDefinition definition,
        int persistedSchemaVersion,
        int persistedVersion,
        IReadOnlyList<GridColumnPreference> columns)
    {
        if (persistedSchemaVersion != definition.SchemaVersion)
        {
            return Default(definition);
        }

        var normalized = ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                persistedSchemaVersion,
                columns,
                persistedVersion));
        return normalized.IsSuccess
            ? new GridPreferenceResponse(
                definition.GridKey,
                definition.SchemaVersion,
                normalized.Value!,
                persistedVersion)
            : Default(definition);
    }

    public static GridPreferenceResponse Default(
        GridPreferenceDefinition definition) =>
        new(definition.GridKey, definition.SchemaVersion, [], 0);

    private static Result<IReadOnlyList<GridColumnPreference>> InvalidPreference() =>
        Failure(
            SettingsErrorCodes.GridPreferenceInvalid,
            "The Grid preference contains an invalid presentation value.",
            ErrorType.Validation);

    private static Result<IReadOnlyList<GridColumnPreference>> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<IReadOnlyList<GridColumnPreference>>.Failure(
            new Error(code, message, type));
}
