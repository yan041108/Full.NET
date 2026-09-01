using Full.NET.Data.CodeGeneration.Naming;

namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 从 INFORMATION_SCHEMA 元数据组装已验证的 FullNetCrudSchema；禁止从目录层推导出默认名称或权限。
/// FAIL-closed：只接受单列 Id 主键；多列主键、非 Id 主键或无可读列一律抛出 NotSupportedException/ArgumentException，不降级猜测。
/// </summary>
internal static class DatabaseCrudSchemaAssembler
{
    /// <summary>
    /// 按显式 Import Options 组装完整 CRUD Schema；主键形态、列映射与关系声明均在此做闭包校验。
    /// 确定性：输出 Schema 的权限码、命名分段与列顺序严格由 options + 列元数据决定，不读取机器文化或时间。
    /// </summary>
    /// <param name="provider">SQL Server 或 MySQL 的方言标识，决定 DatabaseColumnMetadataMapper 的类型映射分支。</param>
    /// <param name="options">显式声明的 OwnerKey/ModuleKey/EntityKey/命名分段/数据作用域与实体能力，禁止目录层猜测。</param>
    /// <param name="columns">从 INFORMATION_SCHEMA.COLUMNS 读出的原始列元数据集合。</param>
    /// <param name="primaryKeyColumns">从 INFORMATION_SCHEMA.KEY_COLUMN_USAGE 读出的主键列集合。</param>
    /// <returns>通过共享命名与 CRUD 不变量校验的 FullNetCrudSchema，可直接进入生成器流水线。</returns>
    public static FullNetCrudSchema Assemble(
        DatabaseMetadataProvider provider,
        DatabaseCrudImportOptions options,
        IReadOnlyList<DatabaseColumnMetadata> columns,
        IReadOnlyList<DatabasePrimaryKeyMetadata> primaryKeyColumns)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(primaryKeyColumns);
        if (columns.Count == 0)
        {
            throw new ArgumentException(
                "目标表不存在或不包含可读取字段。",
                nameof(columns));
        }

        var orderedPrimaryKeyColumns = primaryKeyColumns
            .OrderBy(column => column.OrdinalPosition)
            .Select(column => column.Name)
            .ToArray();
        if (orderedPrimaryKeyColumns.Length != 1
            || !string.Equals(
                orderedPrimaryKeyColumns[0],
                "Id",
                StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "CRUD Schema 导入只支持单列 Id 主键。");
        }

        var tableName = SchemaName.CreateProject(
            options.OwnerKey,
            options.ModuleKey,
            options.EntityKey).Value;
        var mappedColumns = DatabaseColumnMetadataMapper.Map(
            provider,
            columns);
        return options.UsesLegacyEntityCapabilities
            ? FullNetCrudSchema.CreateProject(
                options.OwnerKey,
                options.ModuleKey,
                options.EntityKey,
                tableName,
                options.RootNamespace,
                options.ClrTypeName,
                options.ApiResourceName,
                options.PermissionResourceName,
                options.DataScope,
                options.HasVersion,
                mappedColumns)
            : FullNetCrudSchema.CreateProject(
                options.OwnerKey,
                options.ModuleKey,
                options.EntityKey,
                tableName,
                options.RootNamespace,
                options.ClrTypeName,
                options.ApiResourceName,
                options.PermissionResourceName,
                options.DataScope,
                options.EntityCapabilities,
                mappedColumns);
    }
}

/// <summary>
/// 保存从目录层读取的单个主键列元数据；仅用于 Assemble 时校验主键形态为单列 Id。
/// FAIL-closed：长度 != 1 或列名不等于 "Id"（Ordinal 比较）时 Assemble 立即抛出，不尝试兼容复合键。
/// </summary>
/// <param name="Name">主键列的数据库列名；必须与 "Id" 严格相等。</param>
/// <param name="OrdinalPosition">主键在索引声明中的出现顺序；用于保证单键顺序稳定。</param>
internal sealed record DatabasePrimaryKeyMetadata(
    string Name,
    int OrdinalPosition);
