using Full.NET.Data.CodeGeneration.Naming;

namespace Full.NET.Data.CodeGeneration.Schema;

internal static class DatabaseCrudSchemaAssembler
{
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

internal sealed record DatabasePrimaryKeyMetadata(
    string Name,
    int OrdinalPosition);
