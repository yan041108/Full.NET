using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Serialization;

namespace Full.NET.Modules.Settings.Features.ManageQuerySchemes;

/// <summary>方案元数据不成为 SQL 或权限；所有值按业务发布的当前 Schema 验证并规范化。</summary>
internal static class QuerySchemePolicy
{
    internal const int MaximumOwnedSchemes = 32;
    internal const int MaximumConfigurationBytes = 16 * 1024;

    internal static Result<QuerySchemeConfiguration> Normalize(QuerySchemeDefinition schema, int version, QuerySchemeConfiguration? configuration)
    {
        if (version != schema.SchemaVersion || configuration?.Filters is null || configuration.Columns is null
            || configuration.Filters.Count > 8 || configuration.Columns.Count is < 1 or > 32
            || !schema.SortKeys.Contains(configuration.SortBy, StringComparer.Ordinal)) return Invalid();
        var fields = new HashSet<string>(StringComparer.Ordinal);
        var filters = new List<QuerySchemeFilter>();
        foreach (var filter in configuration.Filters)
        {
            if (filter is null || !fields.Add(filter.FieldKey) || filter.OperatorKey != "contains") return Invalid();
            var field = schema.Filters.FirstOrDefault(item => item.FieldKey == filter.FieldKey);
            if (field is null || string.IsNullOrWhiteSpace(filter.Value) || filter.Value.Length > field.MaxLength
                || filter.Value.Any(char.IsControl)) return Invalid();
            filters.Add(filter with { Value = filter.Value.Trim() });
        }
        var keys = new HashSet<string>(StringComparer.Ordinal); var orders = new HashSet<int>();
        foreach (var column in configuration.Columns)
            if (column is null || !schema.ColumnKeys.Contains(column.ColumnKey, StringComparer.Ordinal) || !keys.Add(column.ColumnKey)
                || column.Order is < 0 or > 31 || !orders.Add(column.Order) || column.Width is < 60 or > 1200
                || column.Fixed is not (null or "left" or "right")) return Invalid();
        if (!configuration.Columns.Any(column => column.Visible)) return Invalid();
        var normalized = configuration with { Filters = filters.OrderBy(filter => filter.FieldKey, StringComparer.Ordinal).ToArray(),
            Columns = configuration.Columns.OrderBy(column => column.Order).ToArray() };
        return JsonSerializer.SerializeToUtf8Bytes(normalized, SettingsJsonSerializerContext.Default.QuerySchemeConfiguration).Length > MaximumConfigurationBytes
            ? Invalid() : Result<QuerySchemeConfiguration>.Success(normalized);
    }

    internal static Error InvalidError() => new(SettingsErrorCodes.QuerySchemeInvalid, "The query scheme does not match the current business schema.", ErrorType.Validation);
    private static Result<QuerySchemeConfiguration> Invalid() => Result<QuerySchemeConfiguration>.Failure(InvalidError());
}
