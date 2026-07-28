using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

internal static class AuditingSqlServerPageStatementBuilder
{
    // 运行时形状只能克隆固定 Host 范围原型，避免动态 SQL 生成改变租户作用域元数据。
    private static readonly SqlStatement HostPagePrototype = new(
        "auditing.page_shape.prototype",
        "SELECT 1",
        SqlDataScope.HostOnly);

    public static SqlStatement[] CreateVariants(
        string statementName,
        string countPrefix,
        string listPrefix,
        string listSuffix,
        IReadOnlyList<string> predicates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementName);
        ArgumentException.ThrowIfNullOrWhiteSpace(countPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(listPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(listSuffix);
        ArgumentNullException.ThrowIfNull(predicates);
        if (predicates.Count is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(predicates),
                "审计 SQL Server 分页只允许 1 到 5 个固定可选谓词。");
        }

        var variants = new SqlStatement[1 << predicates.Count];
        for (var mask = 0; mask < variants.Length; mask++)
        {
            var selectedPredicates = predicates
                .Where((_, index) => (mask & (1 << index)) != 0)
                .ToArray();
            var whereClause = selectedPredicates.Length == 0
                ? string.Empty
                : $"{Environment.NewLine}WHERE {string.Join(
                    $"{Environment.NewLine}  AND ",
                    selectedPredicates)}";
            var count = $"{countPrefix.TrimEnd()}{whereClause}";
            var list =
                $"{listPrefix.TrimEnd()}{whereClause}{Environment.NewLine}{listSuffix.Trim()}";
            variants[mask] = HostPagePrototype with
            {
                Name = statementName,
                Text = $"{count};{Environment.NewLine}{list}",
            };
        }

        return variants;
    }

    public static SqlStatement[] CreateListVariants(
        string statementName,
        string listPrefix,
        string listSuffix,
        IReadOnlyList<string> optionalPredicates,
        IReadOnlyList<string>? requiredPredicates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementName);
        ArgumentException.ThrowIfNullOrWhiteSpace(listPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(listSuffix);
        ArgumentNullException.ThrowIfNull(optionalPredicates);
        if (optionalPredicates.Count is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(optionalPredicates),
                "审计 SQL Server 列表只允许 1 到 5 个固定可选谓词。");
        }

        var stablePredicates = requiredPredicates?.ToArray() ?? [];
        var variants = new SqlStatement[1 << optionalPredicates.Count];
        for (var mask = 0; mask < variants.Length; mask++)
        {
            var selectedPredicates = optionalPredicates
                .Where((_, index) => (mask & (1 << index)) != 0)
                .Concat(stablePredicates)
                .ToArray();
            var whereClause = selectedPredicates.Length == 0
                ? string.Empty
                : $"{Environment.NewLine}WHERE {string.Join(
                    $"{Environment.NewLine}  AND ",
                    selectedPredicates)}";
            variants[mask] = HostPagePrototype with
            {
                Name = statementName,
                Text =
                    $"{listPrefix.TrimEnd()}{whereClause}{Environment.NewLine}{listSuffix.Trim()}",
            };
        }

        return variants;
    }
}
