using Full.NET.Data.CodeGeneration.Naming;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>统一生成预检查与双库约束采用的唯一键，软删记录继续占用键值。</summary>
internal static class CrudUniqueKeyGenerator
{
    internal static IReadOnlyList<IReadOnlyList<string>> Keys(FullNetCrudSchema schema)
    {
        var keys = schema.Columns.Where(column => column.Ui?.Unique == true)
            .Select(column => (IReadOnlyList<string>)new[] { column.DatabaseName }).ToList();
        keys.AddRange(schema.Relationships.Where(relationship => relationship.DependentEntityKey == schema.EntityKey
                && relationship.CompositeKeyColumnNames is { Count: > 0 })
            .Select(relationship => relationship.CompositeKeyColumnNames!));
        if (schema.Scene == FullNetCrudScene.ManyToMany)
        {
            keys.Add(schema.Relationships.Select(relationship => relationship.DependentColumnName).ToArray());
        }

        return keys.Select(key => (IReadOnlyList<string>)(schema.IsTenantScoped
                ? new[] { "TenantId" }.Concat(key.Where(name => name != "TenantId")).ToArray() : key.ToArray()))
            .DistinctBy(key => string.Join("_", key), StringComparer.Ordinal)
            .Select(key =>
            {
                var columns = key.Select(name => schema.Columns.SingleOrDefault(column => column.DatabaseName == name)
                    ?? throw new ArgumentException("复合唯一键引用了不存在的列。")).ToArray();
                if (key.Distinct(StringComparer.Ordinal).Count() != key.Count
                    || columns.Sum(KeyBytes) > 900)
                {
                    throw new ArgumentException("唯一键重复或超出双库安全索引长度，必须明确缩小键定义。");
                }
                return key;
            }).ToArray();
    }

    internal static string Name(FullNetCrudSchema schema, IReadOnlyList<string> key) =>
        DatabaseObjectNameBuilder.Build($"UX_{schema.DatabaseTableName}_{string.Join("_", key)}");

    private static int KeyBytes(FullNetColumn column) => column.ScalarType switch
    {
        FullNetScalarType.String => column.MaxLength!.Value > 225 ? 901 : column.MaxLength.Value * 4,
        FullNetScalarType.Uuid => 16,
        FullNetScalarType.Boolean => 1,
        FullNetScalarType.Int32 => 4,
        FullNetScalarType.Int64 => 8,
        _ => 17,
    };
}
