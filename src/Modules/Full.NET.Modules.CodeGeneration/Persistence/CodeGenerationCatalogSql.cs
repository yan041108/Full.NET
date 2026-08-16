using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// Host 只读扫描当前库基础表与列；语句必须保持 HostOnly，且与 CLI 目录查询共用 SQL 文本。
/// </summary>
internal static class CodeGenerationCatalogSql
{
    public static readonly SqlStatement ListTablesSqlServer = new(
        "codegen.catalog.list_tables.sql_server",
        DatabaseCatalogQueries.ListTablesSqlServer,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListTablesMySql = new(
        "codegen.catalog.list_tables.my_sql",
        DatabaseCatalogQueries.ListTablesMySql,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListColumnsSqlServer = new(
        "codegen.catalog.list_columns.sql_server",
        DatabaseCatalogQueries.ListColumnsSqlServer,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListColumnsMySql = new(
        "codegen.catalog.list_columns.my_sql",
        DatabaseCatalogQueries.ListColumnsMySql,
        SqlDataScope.HostOnly);
}

internal sealed class CodeGenerationCatalogTableRow
{
    public required string TableName { get; init; }
}

internal sealed class CodeGenerationCatalogColumnRow
{
    public required string ColumnName { get; init; }

    public required string DataType { get; init; }

    public required string ColumnType { get; init; }

    public required string IsNullable { get; init; }

    public long? MaxLength { get; init; }

    public int? NumericPrecision { get; init; }

    public int? NumericScale { get; init; }

    public int OrdinalPosition { get; init; }
}
