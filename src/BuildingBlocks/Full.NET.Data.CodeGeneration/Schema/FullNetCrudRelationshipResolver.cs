namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>从已冻结的实体定义解析关系，不依赖数据库或可变模板目录。</summary>
public static class FullNetCrudRelationshipResolver
{
    /// <summary>取得主端实体；当前实体使用自身，其余必须由输入明确提供。</summary>
    /// <param name="current">当前经过校验的生成实体。</param>
    /// <param name="relationship">携带叶子快照的关系定义。</param>
    public static FullNetCrudSchema Principal(FullNetCrudSchema current, FullNetCrudRelationship relationship) =>
        Resolve(current, relationship.PrincipalEntityKey, relationship.PrincipalSchema);

    /// <summary>取得从端实体并校验所有权、作用域与叶子快照。</summary>
    /// <param name="current">当前经过校验的生成实体。</param>
    /// <param name="relationship">携带叶子快照的关系定义。</param>
    public static FullNetCrudSchema Dependent(FullNetCrudSchema current, FullNetCrudRelationship relationship) =>
        Resolve(current, relationship.DependentEntityKey, relationship.DependentSchema);

    /// <summary>验证可执行关系引用的真实列、唯一性和级联能力。</summary>
    internal static void Validate(FullNetCrudSchema current, FullNetCrudRelationship relationship)
    {
        var principal = Principal(current, relationship);
        var dependent = Dependent(current, relationship);
        var parentColumn = principal.Columns.SingleOrDefault(column => column.DatabaseName == relationship.PrincipalColumnName);
        var childColumn = dependent.Columns.SingleOrDefault(column => column.DatabaseName == relationship.DependentColumnName);
        if (parentColumn is null || childColumn is null
            || parentColumn.ScalarType != childColumn.ScalarType
            || parentColumn.IsNullable
            || (parentColumn.DatabaseName != "Id" && parentColumn.Ui?.Unique != true))
        {
            throw new ArgumentException("关系必须引用同类型的主键或显式非空唯一列。");
        }

        if (relationship.CascadeDelete == true && !dependent.EntityCapabilities.CanDelete)
        {
            throw new ArgumentException("不可变从端禁止级联删除。");
        }

        if (relationship.CompositeKeyColumnNames is { } key
            && (key.Count == 0 || key.Distinct(StringComparer.Ordinal).Count() != key.Count
                || key.Any(name => dependent.Columns.All(column => column.DatabaseName != name))))
        {
            throw new ArgumentException("关系复合唯一键必须引用从端的不同实际列。");
        }
    }

    private static FullNetCrudSchema Resolve(FullNetCrudSchema current, string key, FullNetCrudSchema? snapshot)
    {
        if (key == current.EntityKey)
        {
            if (snapshot is not null) throw new ArgumentException("当前实体不允许提供第二份关系快照。");
            return current;
        }

        if (snapshot is null
            || snapshot.UsesLegacyEntityCapabilities
            || snapshot.OwnerKey != current.OwnerKey || snapshot.ModuleKey != current.ModuleKey
            || snapshot.EntityKey != key || snapshot.DataScope != current.DataScope
            || snapshot.EntityCapabilities.OwnershipMode != current.EntityCapabilities.OwnershipMode
            || snapshot.Scene != FullNetCrudScene.Single || snapshot.Relationships.Count != 0)
        {
            throw new ArgumentException("关系引用必须提供声明显式实体能力、同所有者、同模块、同作用域和同组织归属模式的单实体快照。");
        }

        return snapshot;
    }
}
