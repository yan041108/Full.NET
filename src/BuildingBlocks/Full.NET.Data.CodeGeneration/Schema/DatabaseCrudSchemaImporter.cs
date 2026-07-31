using System.Data;
using System.Data.Common;
using System.Globalization;
using Full.NET.Data.CodeGeneration.Naming;

namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 从已打开的数据库连接只读导入单表元数据，并生成经过统一不变量校验的 CRUD Schema。
/// </summary>
public static class DatabaseCrudSchemaImporter
{
    // 两条查询都固定到默认 Schema，并通过约束名、表与 Schema 联合连接，避免同名约束串表。
    private const string SqlServerColumnsSql =
        """
        SELECT COLUMN_NAME AS ColumnName,
               DATA_TYPE AS DataType,
               DATA_TYPE AS ColumnType,
               IS_NULLABLE AS IsNullable,
               CHARACTER_MAXIMUM_LENGTH AS MaxLength,
               NUMERIC_PRECISION AS NumericPrecision,
               NUMERIC_SCALE AS NumericScale,
               ORDINAL_POSITION AS OrdinalPosition
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = 'dbo'
          AND TABLE_NAME = @TableName
        ORDER BY ORDINAL_POSITION
        """;

    private const string SqlServerPrimaryKeySql =
        """
        SELECT keyColumn.COLUMN_NAME AS ColumnName,
               keyColumn.ORDINAL_POSITION AS OrdinalPosition
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tableConstraint
        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS keyColumn
            ON keyColumn.CONSTRAINT_CATALOG = tableConstraint.CONSTRAINT_CATALOG
           AND keyColumn.CONSTRAINT_SCHEMA = tableConstraint.CONSTRAINT_SCHEMA
           AND keyColumn.CONSTRAINT_NAME = tableConstraint.CONSTRAINT_NAME
           AND keyColumn.TABLE_CATALOG = tableConstraint.TABLE_CATALOG
           AND keyColumn.TABLE_SCHEMA = tableConstraint.TABLE_SCHEMA
           AND keyColumn.TABLE_NAME = tableConstraint.TABLE_NAME
        WHERE tableConstraint.CONSTRAINT_TYPE = 'PRIMARY KEY'
          AND tableConstraint.TABLE_SCHEMA = 'dbo'
          AND tableConstraint.TABLE_NAME = @TableName
        ORDER BY keyColumn.ORDINAL_POSITION
        """;

    private const string MySqlColumnsSql =
        """
        SELECT COLUMN_NAME AS ColumnName,
               DATA_TYPE AS DataType,
               COLUMN_TYPE AS ColumnType,
               IS_NULLABLE AS IsNullable,
               CHARACTER_MAXIMUM_LENGTH AS MaxLength,
               NUMERIC_PRECISION AS NumericPrecision,
               NUMERIC_SCALE AS NumericScale,
               ORDINAL_POSITION AS OrdinalPosition
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = @TableName
        ORDER BY ORDINAL_POSITION
        """;

    private const string MySqlPrimaryKeySql =
        """
        SELECT keyColumn.COLUMN_NAME AS ColumnName,
               keyColumn.ORDINAL_POSITION AS OrdinalPosition
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tableConstraint
        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS keyColumn
            ON keyColumn.CONSTRAINT_SCHEMA = tableConstraint.CONSTRAINT_SCHEMA
           AND keyColumn.CONSTRAINT_NAME = tableConstraint.CONSTRAINT_NAME
           AND keyColumn.TABLE_SCHEMA = tableConstraint.TABLE_SCHEMA
           AND keyColumn.TABLE_NAME = tableConstraint.TABLE_NAME
        WHERE tableConstraint.CONSTRAINT_TYPE = 'PRIMARY KEY'
          AND tableConstraint.TABLE_SCHEMA = DATABASE()
          AND tableConstraint.TABLE_NAME = @TableName
        ORDER BY keyColumn.ORDINAL_POSITION
        """;

    /// <summary>
    /// 读取目标表的列与主键元数据并生成 CRUD Schema；连接由调用方创建、打开和释放。
    /// </summary>
    /// <param name="connection">已打开且指向目标数据库的连接。</param>
    /// <param name="provider">元数据方言。</param>
    /// <param name="options">数据库无法可靠推导的稳定契约名称与 CRUD 边界。</param>
    /// <param name="cancellationToken">用于取消元数据查询的令牌。</param>
    /// <returns>经过 Naming Profile、主键、租户和版本不变量校验的 Schema。</returns>
    public static async Task<FullNetCrudSchema> ImportAsync(
        DbConnection connection,
        DatabaseMetadataProvider provider,
        DatabaseCrudImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "数据库元数据导入要求调用方先打开连接。");
        }

        var tableName = SchemaName.CreateProject(
            options.OwnerKey,
            options.ModuleKey,
            options.EntityKey).Value;
        var (columnsSql, primaryKeySql) = provider switch
        {
            DatabaseMetadataProvider.SqlServer => (
                SqlServerColumnsSql,
                SqlServerPrimaryKeySql),
            DatabaseMetadataProvider.MySql => (
                MySqlColumnsSql,
                MySqlPrimaryKeySql),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                null),
        };

        var columns = await ReadColumnsAsync(
            connection,
            columnsSql,
            tableName,
            cancellationToken);
        var primaryKeyColumns = await ReadPrimaryKeyColumnsAsync(
            connection,
            primaryKeySql,
            tableName,
            cancellationToken);
        return DatabaseCrudSchemaAssembler.Assemble(
            provider,
            options,
            columns,
            primaryKeyColumns);
    }

    private static async Task<IReadOnlyList<DatabaseColumnMetadata>> ReadColumnsAsync(
        DbConnection connection,
        string sql,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, sql, tableName);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var columns = new List<DatabaseColumnMetadata>();
        var columnNameOrdinal = reader.GetOrdinal("ColumnName");
        var dataTypeOrdinal = reader.GetOrdinal("DataType");
        var columnTypeOrdinal = reader.GetOrdinal("ColumnType");
        var isNullableOrdinal = reader.GetOrdinal("IsNullable");
        var maxLengthOrdinal = reader.GetOrdinal("MaxLength");
        var numericPrecisionOrdinal = reader.GetOrdinal("NumericPrecision");
        var numericScaleOrdinal = reader.GetOrdinal("NumericScale");
        var positionOrdinal = reader.GetOrdinal("OrdinalPosition");
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new DatabaseColumnMetadata(
                reader.GetString(columnNameOrdinal),
                reader.GetString(dataTypeOrdinal),
                reader.GetString(columnTypeOrdinal),
                string.Equals(
                    reader.GetString(isNullableOrdinal),
                    "YES",
                    StringComparison.OrdinalIgnoreCase),
                reader.IsDBNull(maxLengthOrdinal)
                    ? null
                    : Convert.ToInt64(
                        reader.GetValue(maxLengthOrdinal),
                        CultureInfo.InvariantCulture),
                Convert.ToInt32(
                    reader.GetValue(positionOrdinal),
                    CultureInfo.InvariantCulture),
                reader.IsDBNull(numericPrecisionOrdinal)
                    ? null
                    : Convert.ToInt32(
                        reader.GetValue(numericPrecisionOrdinal),
                        CultureInfo.InvariantCulture),
                reader.IsDBNull(numericScaleOrdinal)
                    ? null
                    : Convert.ToInt32(
                        reader.GetValue(numericScaleOrdinal),
                        CultureInfo.InvariantCulture)));
        }

        return columns;
    }

    private static async Task<IReadOnlyList<DatabasePrimaryKeyMetadata>>
        ReadPrimaryKeyColumnsAsync(
            DbConnection connection,
            string sql,
            string tableName,
            CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, sql, tableName);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var columns = new List<DatabasePrimaryKeyMetadata>();
        var columnNameOrdinal = reader.GetOrdinal("ColumnName");
        var positionOrdinal = reader.GetOrdinal("OrdinalPosition");
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new DatabasePrimaryKeyMetadata(
                reader.GetString(columnNameOrdinal),
                Convert.ToInt32(
                    reader.GetValue(positionOrdinal),
                    CultureInfo.InvariantCulture)));
        }

        return columns;
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        string sql,
        string tableName)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@TableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return command;
    }
}
