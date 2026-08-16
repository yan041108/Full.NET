namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 集中保存只读数据库目录查询，供 CLI 连接读取与 Host IQueryExecutor 共用，避免双写漂移。
/// </summary>
public static class DatabaseCatalogQueries
{
    /// <summary>SQL Server 默认 dbo 基础表，排除视图。</summary>
    public const string ListTablesSqlServer =
        """
        SELECT TABLE_NAME AS TableName
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = 'dbo'
          AND TABLE_TYPE = 'BASE TABLE'
        """;

    /// <summary>MySQL 当前库基础表，排除视图。</summary>
    public const string ListTablesMySql =
        """
        SELECT TABLE_NAME AS TableName
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_TYPE = 'BASE TABLE'
        """;

    /// <summary>SQL Server 单表列元数据，按序数排序。</summary>
    public const string ListColumnsSqlServer =
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

    /// <summary>MySQL 单表列元数据，按序数排序。</summary>
    public const string ListColumnsMySql =
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

    /// <summary>
    /// 目录表名只允许 ASCII 标识符，防止把路径或通配送进元数据查询。
    /// </summary>
    public static bool IsSafeTableName(string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName)
            || tableName.Length > 64)
        {
            return false;
        }

        if (!char.IsAsciiLetter(tableName[0]))
        {
            return false;
        }

        for (var index = 1; index < tableName.Length; index++)
        {
            var character = tableName[index];
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
