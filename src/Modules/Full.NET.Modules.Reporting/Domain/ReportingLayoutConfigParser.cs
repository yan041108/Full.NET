using System.Text.Json;
using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>布局配置中的列定义；用于显示名称与列级权限。</summary>
internal sealed record ReportingLayoutColumnDefinition(
    string ColumnKey,
    string DisplayName,
    string? RequiredPermission);

/// <summary>解析报表布局配置 JSON。</summary>
internal static class ReportingLayoutConfigParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>从布局配置 JSON 解析列定义；缺失时返回空集合。</summary>
    public static IReadOnlyList<ReportingLayoutColumnDefinition> ParseColumns(string layoutConfigJson)
    {
        if (string.IsNullOrWhiteSpace(layoutConfigJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(layoutConfigJson);
        if (!document.RootElement.TryGetProperty("columns", out var columnsElement)
            || columnsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var columns = new List<ReportingLayoutColumnDefinition>();
        foreach (var columnElement in columnsElement.EnumerateArray())
        {
            if (!columnElement.TryGetProperty("key", out var keyElement)
                || keyElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var columnKey = keyElement.GetString()?.Trim();
            if (string.IsNullOrEmpty(columnKey))
            {
                continue;
            }

            var displayName = columnElement.TryGetProperty("displayName", out var displayElement)
                              && displayElement.ValueKind == JsonValueKind.String
                ? displayElement.GetString()?.Trim()
                : null;
            var requiredPermission = columnElement.TryGetProperty("requiredPermission", out var permissionElement)
                                   && permissionElement.ValueKind == JsonValueKind.String
                ? permissionElement.GetString()?.Trim()
                : null;
            columns.Add(new ReportingLayoutColumnDefinition(
                columnKey,
                string.IsNullOrEmpty(displayName) ? columnKey : displayName,
                string.IsNullOrEmpty(requiredPermission) ? null : requiredPermission));
        }

        return columns;
    }

    /// <summary>根据布局列定义与列权限过滤可见列。</summary>
    public static IReadOnlyList<ReportingExecutionColumnDefinition> ResolveVisibleColumns(
        IReadOnlyList<ReportingLayoutColumnDefinition> layoutColumns,
        IReadOnlyList<string> resultColumnKeys,
        Func<string, bool> hasPermission)
    {
        if (layoutColumns.Count > 0)
        {
            return layoutColumns
                .Where(column =>
                    resultColumnKeys.Contains(column.ColumnKey, StringComparer.Ordinal)
                    && (column.RequiredPermission is null || hasPermission(column.RequiredPermission)))
                .Select(column => new ReportingExecutionColumnDefinition(column.ColumnKey, column.DisplayName))
                .ToArray();
        }

        return resultColumnKeys
            .Select(columnKey => new ReportingExecutionColumnDefinition(columnKey, columnKey))
            .ToArray();
    }
}
