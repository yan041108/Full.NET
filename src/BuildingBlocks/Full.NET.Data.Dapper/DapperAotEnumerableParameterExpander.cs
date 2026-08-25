using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 将 Dapper 风格的集合参数展开为标量占位符，供 Native AOT 手工 ADO.NET 绑定使用。
/// </summary>
/// <remarks>
/// 反射版 Dapper 会把 <c>IN @Ids</c> 与 <c>Guid[]</c> 展开为 <c>IN (@Ids0,@Ids1,...)</c>；
/// AOT 路径绕过 SqlMapper，若不做同等展开，SqlClient/MySqlConnector 会拒绝 <c>Guid[]</c> 参数。
/// </remarks>
internal static class DapperAotEnumerableParameterExpander
{
    public static (string Sql, DynamicParameters Parameters) Expand(
        string sql,
        DynamicParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!parameters.ParameterNames.Any(name =>
                TryGetExpandableItems(
                    parameters.Get<object>(name),
                    out _)))
        {
            return (sql, parameters);
        }

        var expanded = new DynamicParameters();
        var resultSql = sql;
        foreach (var name in parameters.ParameterNames)
        {
            var value = parameters.Get<object>(name);
            if (!TryGetExpandableItems(value, out var items))
            {
                expanded.Add(name, value);
                continue;
            }

            if (items.Count == 0)
            {
                // 与 Dapper 空集合语义对齐：生成恒假子查询，避免非法的 IN ()。
                resultSql = ReplaceParameterToken(
                    resultSql,
                    name,
                    "(SELECT NULL WHERE 1 = 0)");
                continue;
            }

            var placeholders = new string[items.Count];
            for (var index = 0; index < items.Count; index++)
            {
                var itemName = name + index.ToString(CultureInfo.InvariantCulture);
                placeholders[index] = "@" + itemName;
                expanded.Add(itemName, items[index]);
            }

            resultSql = ReplaceParameterToken(
                resultSql,
                name,
                "(" + string.Join(",", placeholders) + ")");
        }

        return (resultSql, expanded);
    }

    private static bool TryGetExpandableItems(
        object? value,
        out IReadOnlyList<object?> items)
    {
        items = Array.Empty<object?>();
        if (value is null or string or byte[])
        {
            return false;
        }

        if (value is not IEnumerable enumerable)
        {
            return false;
        }

        var list = new List<object?>();
        foreach (var item in enumerable)
        {
            list.Add(item);
        }

        items = list;
        return true;
    }

    private static string ReplaceParameterToken(
        string sql,
        string name,
        string replacement)
    {
        // (?!\w) 防止把 @Ids 误匹配成 @Ids0 的前缀。
        var pattern = @"([?@:])" + Regex.Escape(name) + @"(?!\w)";
        return Regex.Replace(
            sql,
            pattern,
            _ => replacement,
            RegexOptions.CultureInvariant);
    }
}
