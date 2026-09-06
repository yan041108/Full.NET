using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>为静态 Query Port 构造带分页与行数上限的执行 SQL。</summary>
internal static class ReportingExecutionSqlBuilder
{
    /// <summary>构造分页执行 SQL 与 Dapper 参数。</summary>
    public static (string Sql, IReadOnlyDictionary<string, object?> Parameters) Build(
        string queryPortKey,
        string providerKey,
        IReadOnlyDictionary<string, object?> boundParameters,
        int page,
        int pageSize)
    {
        var offset = (page - 1) * pageSize;
        var baseSql = ReportingQueryPortCatalog.ResolveBaseSql(queryPortKey, providerKey)
            ?? throw new InvalidOperationException($"Query port '{queryPortKey}' is not supported for provider '{providerKey}'.");

        if (!ReportingQueryPortCatalog.SupportsPagination(queryPortKey))
        {
            return (baseSql, boundParameters);
        }

        var orderByColumn = ReportingQueryPortCatalog.ResolveOrderByColumn(queryPortKey)
            ?? throw new InvalidOperationException($"Query port '{queryPortKey}' does not declare an order column.");

        var parameters = new Dictionary<string, object?>(boundParameters, StringComparer.Ordinal)
        {
            ["Offset"] = offset,
            ["PageSize"] = pageSize,
        };

        if (string.Equals(providerKey, ReportingDataSourceProviderKeys.SqlServer, StringComparison.Ordinal))
        {
            var cappedSql = ApplyTopNCapSqlServer(queryPortKey, baseSql, boundParameters);
            var sql = $"""
                SELECT *
                FROM (
                    {cappedSql}
                ) AS reporting_q
                ORDER BY [{orderByColumn}]
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            return (sql, parameters);
        }

        if (string.Equals(providerKey, ReportingDataSourceProviderKeys.MySql, StringComparison.Ordinal))
        {
            var cappedSql = ApplyTopNCapMySql(queryPortKey, baseSql, boundParameters);
            var sql = $"""
                SELECT *
                FROM (
                    {cappedSql}
                ) AS reporting_q
                ORDER BY `{orderByColumn}`
                LIMIT @PageSize OFFSET @Offset
                """;
            return (sql, parameters);
        }

        throw new InvalidOperationException($"Provider '{providerKey}' is not supported.");
    }

    private static string ApplyTopNCapSqlServer(
        string queryPortKey,
        string baseSql,
        IReadOnlyDictionary<string, object?> boundParameters) =>
        string.Equals(queryPortKey, "reporting.schema_inventory", StringComparison.Ordinal)
            ? "SELECT TOP (@TopN) name AS SchemaName FROM sys.schemas ORDER BY name"
            : baseSql;

    private static string ApplyTopNCapMySql(
        string queryPortKey,
        string baseSql,
        IReadOnlyDictionary<string, object?> boundParameters) =>
        string.Equals(queryPortKey, "reporting.schema_inventory", StringComparison.Ordinal)
            ? "SELECT schema_name AS SchemaName FROM information_schema.schemata ORDER BY schema_name LIMIT @TopN"
            : baseSql;
}
