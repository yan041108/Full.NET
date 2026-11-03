namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>查询参数从声明的列生成，提前拒绝类型不匹配与范围后缀碰撞，避免生成不可编译契约。</summary>
internal static class FullNetCrudQueryDefinition
{
    internal sealed record Field(FullNetColumn Column, string ClrName, string QueryName, string Suffix);

    internal static IReadOnlyList<Field> Fields(IReadOnlyList<FullNetColumn> columns)
    {
        var fields = new List<Field>();
        foreach (var column in columns)
        {
            ArgumentNullException.ThrowIfNull(column);
            if (!column.ResolvedUi.Queryable || column.ResolvedUi.QueryKind == FullNetColumnQueryKind.None) continue;
            var kind = column.ResolvedUi.QueryKind;
            if (!Enum.IsDefined(kind) || kind == FullNetColumnQueryKind.Contains && column.ScalarType != FullNetScalarType.String
                || kind == FullNetColumnQueryKind.Range && column.ScalarType is not (FullNetScalarType.Int32 or FullNetScalarType.Int64 or FullNetScalarType.Decimal or FullNetScalarType.DateTimeUtc))
                throw new ArgumentException("查询比较方式与字段类型不匹配。");
            foreach (var suffix in kind == FullNetColumnQueryKind.Range ? new[] { "From", "To" } : [string.Empty])
                fields.Add(new(column, "Filter" + column.ClrPropertyName + suffix, "filter" + column.ClrPropertyName + suffix, suffix));
        }
        if (fields.Select(field => field.ClrName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Count)
            throw new ArgumentException("范围查询参数与其他字段名称冲突，请显式调整查询声明。");
        return fields;
    }
}
