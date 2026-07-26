using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>在租户作用域 SQL 上追加数据范围过滤片段。</summary>
internal static class TenantScopedSqlComposer
{
    private const string TenantWhereAnchor = "WHERE TenantId = @TenantId";

    internal const string AssignmentTenantWhereAnchor = "WHERE assignment.TenantId = @TenantId";

    internal static SqlStatement ApplyDataScopeFilter(
        SqlStatement statement,
        DataScopeSqlFilter? filter,
        string tenantWhereAnchor = TenantWhereAnchor)
    {
        if (filter is null)
        {
            return statement;
        }

        var text = InjectFilter(statement.Text, filter.Sql, tenantWhereAnchor);
        return statement with
        {
            Name = statement.Name + ".data_scope",
            Text = text,
        };
    }

    internal static object? MergeParameters(object? queryParameters, DataScopeSqlFilter? filter)
    {
        if (filter?.Parameters is null)
        {
            return queryParameters;
        }

        if (queryParameters is null)
        {
            return filter.Parameters;
        }

        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        CopyProperties(queryParameters, merged);
        CopyProperties(filter.Parameters, merged);
        return merged;
    }

    private static string InjectFilter(string sql, string condition, string tenantWhereAnchor)
    {
        var index = sql.IndexOf(tenantWhereAnchor, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException(
                "Tenant-scoped SQL must contain the tenant boundary anchor.");
        }

        var insertAt = index + tenantWhereAnchor.Length;
        return sql.Insert(insertAt, $" AND ({condition})");
    }

    private static void CopyProperties(
        object source,
        IDictionary<string, object?> target)
    {
        if (source is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                target[pair.Key] = pair.Value;
            }

            return;
        }

        foreach (var property in source.GetType().GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            target[property.Name] = property.GetValue(source);
        }
    }
}
