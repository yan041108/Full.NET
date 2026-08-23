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

    internal static IReadOnlyDictionary<string, object?>? MergeParameters(
        IReadOnlyDictionary<string, object?>? queryParameters,
        DataScopeSqlFilter? filter)
    {
        if (queryParameters is null && filter?.Parameters is null)
        {
            return queryParameters;
        }

        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (queryParameters is not null)
        {
            foreach (var pair in queryParameters)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        MergeFilterParameters(filter, merged);
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

    private static void MergeFilterParameters(
        DataScopeSqlFilter? filter,
        Dictionary<string, object?> merged)
    {
        if (filter?.Parameters is null)
        {
            return;
        }

        if (filter.Parameters is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            foreach (var pair in readOnlyDictionary)
            {
                merged[pair.Key] = pair.Value;
            }

            return;
        }

        if (filter.Parameters is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                merged[pair.Key] = pair.Value;
            }

            return;
        }

        throw new InvalidOperationException(
            "Data scope parameters must be dictionary-based for AOT-safe merging.");
    }
}
