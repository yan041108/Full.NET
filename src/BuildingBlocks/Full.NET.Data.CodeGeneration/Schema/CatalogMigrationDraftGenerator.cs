using System.Text;
using Full.NET.Data.CodeGeneration.Naming;

namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 根据只读目录列元数据生成双库 DbUp 迁移草案文本，不执行 DDL。
/// </summary>
public static class CatalogMigrationDraftGenerator
{
    /// <summary>
    /// 根据源 Provider 的 INFORMATION_SCHEMA 列快照生成 SQL Server 与 MySQL 建表草案。
    /// </summary>
    /// <param name="tableName">物理表名，必须为安全 ASCII 标识符。</param>
    /// <param name="sourceProvider">列元数据来源的数据库方言。</param>
    /// <param name="columns">按序数排序的原始列元数据。</param>
    /// <returns>双库草案文本与跨方言映射警告。</returns>
    public static CatalogMigrationDraftResult Generate(
        string tableName,
        DatabaseMetadataProvider sourceProvider,
        IReadOnlyList<DatabaseColumnMetadata> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
        {
            throw new ArgumentException(
                "迁移草案要求至少一列。",
                nameof(columns));
        }

        var ordered = columns
            .OrderBy(column => column.OrdinalPosition)
            .ToArray();
        var warnings = new List<string>();
        var sqlServerDraft = sourceProvider == DatabaseMetadataProvider.SqlServer
            ? RenderNativeDraft(tableName, ordered, DatabaseMetadataProvider.SqlServer)
            : RenderMappedDraft(
                tableName,
                ordered,
                sourceProvider,
                DatabaseMetadataProvider.SqlServer,
                warnings);
        var mySqlDraft = sourceProvider == DatabaseMetadataProvider.MySql
            ? RenderNativeDraft(tableName, ordered, DatabaseMetadataProvider.MySql)
            : RenderMappedDraft(
                tableName,
                ordered,
                sourceProvider,
                DatabaseMetadataProvider.MySql,
                warnings);

        return new CatalogMigrationDraftResult(
            sqlServerDraft,
            mySqlDraft,
            warnings
                .Distinct(StringComparer.Ordinal)
                .OrderBy(warning => warning, StringComparer.Ordinal)
                .ToArray());
    }

    private static string RenderNativeDraft(
        string tableName,
        IReadOnlyList<DatabaseColumnMetadata> columns,
        DatabaseMetadataProvider provider)
    {
        var columnLines = columns
            .Select(column =>
                $"{column.Name} {column.ColumnType} "
                + (column.IsNullable ? "NULL" : "NOT NULL"))
            .ToArray();
        return provider == DatabaseMetadataProvider.SqlServer
            ? RenderSqlServerCreate(tableName, columnLines, hasIdPrimaryKey: HasIdColumn(columns))
            : RenderMySqlCreate(tableName, columnLines, hasIdPrimaryKey: HasIdColumn(columns));
    }

    private static string RenderMappedDraft(
        string tableName,
        IReadOnlyList<DatabaseColumnMetadata> columns,
        DatabaseMetadataProvider sourceProvider,
        DatabaseMetadataProvider targetProvider,
        IList<string> warnings)
    {
        var mappedColumns = new List<(string Name, string Type, bool IsNullable)>();
        foreach (var column in columns)
        {
            if (!DatabaseColumnMetadataMapper.TryMap(
                    sourceProvider,
                    column,
                    out var mapped))
            {
                warnings.Add(
                    $"列 {column.Name} 无法从 {sourceProvider} 映射到 {targetProvider}，已从草案省略。");
                continue;
            }

            mappedColumns.Add((
                mapped.DatabaseName,
                RenderMappedType(mapped, targetProvider),
                mapped.IsNullable));
        }

        if (mappedColumns.Count == 0)
        {
            throw new InvalidOperationException(
                "没有可映射的列，无法生成跨方言迁移草案。");
        }

        var columnLines = mappedColumns
            .Select(column =>
                $"{column.Name} {column.Type} "
                + (column.IsNullable ? "NULL" : "NOT NULL"))
            .ToArray();
        warnings.Add(
            $"草案由 {sourceProvider} 元数据映射生成 {targetProvider} 类型，提交前必须人工复核。");
        return targetProvider == DatabaseMetadataProvider.SqlServer
            ? RenderSqlServerCreate(
                tableName,
                columnLines,
                hasIdPrimaryKey: mappedColumns.Any(column =>
                    string.Equals(column.Name, "Id", StringComparison.Ordinal)))
            : RenderMySqlCreate(
                tableName,
                columnLines,
                hasIdPrimaryKey: mappedColumns.Any(column =>
                    string.Equals(column.Name, "Id", StringComparison.Ordinal)));
    }

    private static string RenderMappedType(
        FullNetColumn column,
        DatabaseMetadataProvider provider) =>
        (provider, column.ScalarType) switch
        {
            (DatabaseMetadataProvider.SqlServer, FullNetScalarType.Uuid) =>
                "uniqueidentifier",
            (DatabaseMetadataProvider.MySql, FullNetScalarType.Uuid) =>
                "BINARY(16)",
            (DatabaseMetadataProvider.SqlServer, FullNetScalarType.String) =>
                $"nvarchar({column.MaxLength})",
            (DatabaseMetadataProvider.MySql, FullNetScalarType.String) =>
                $"varchar({column.MaxLength})",
            (_, FullNetScalarType.Int32) => "int",
            (_, FullNetScalarType.Int64) => "bigint",
            (DatabaseMetadataProvider.SqlServer, FullNetScalarType.Boolean) =>
                "bit",
            (DatabaseMetadataProvider.MySql, FullNetScalarType.Boolean) =>
                "boolean",
            (DatabaseMetadataProvider.SqlServer, FullNetScalarType.DateTimeUtc) =>
                "datetimeoffset(7)",
            (DatabaseMetadataProvider.MySql, FullNetScalarType.DateTimeUtc) =>
                "datetime(6)",
            (_, FullNetScalarType.Decimal) =>
                $"decimal({column.NumericPrecision}, {column.NumericScale})",
            _ => throw new ArgumentOutOfRangeException(
                nameof(column),
                column.ScalarType,
                "不支持的迁移草案字段类型。"),
        };

    private static string RenderSqlServerCreate(
        string tableName,
        IReadOnlyList<string> columnLines,
        bool hasIdPrimaryKey)
    {
        var primaryKey = hasIdPrimaryKey
            ? $",\n        CONSTRAINT {DatabaseObjectNameBuilder.Build($"PK_{tableName}")} "
                + "PRIMARY KEY CLUSTERED (Id)"
            : string.Empty;
        return Normalize(
            $$"""
            -- Full.NET 目录迁移草案（SQL Server）。
            -- 请分配真实迁移编号并完成幂等恢复评审后，再移入正式 DbUp 目录。
            IF OBJECT_ID(N'dbo.{{tableName}}', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.{{tableName}}
                (
            {{IndentLines(string.Join(",\n", columnLines), 8)}}{{primaryKey}}
                );
            END;
            """);
    }

    private static string RenderMySqlCreate(
        string tableName,
        IReadOnlyList<string> columnLines,
        bool hasIdPrimaryKey)
    {
        var primaryKey = hasIdPrimaryKey
            ? $",\n    CONSTRAINT {DatabaseObjectNameBuilder.Build($"PK_{tableName}")} "
                + "PRIMARY KEY (Id)"
            : string.Empty;
        return Normalize(
            $$"""
            -- Full.NET 目录迁移草案（MySQL）。
            -- 请分配真实迁移编号并完成幂等恢复评审后，再移入正式 DbUp 目录。
            CREATE TABLE IF NOT EXISTS {{tableName}}
            (
            {{IndentLines(string.Join(",\n", columnLines), 4)}}{{primaryKey}}
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
            """);
    }

    private static bool HasIdColumn(IReadOnlyList<DatabaseColumnMetadata> columns) =>
        columns.Any(column =>
            string.Equals(column.Name, "Id", StringComparison.Ordinal));

    private static string IndentLines(string content, int spaces)
    {
        var indentation = new string(' ', spaces);
        return string.Join(
            "\n",
            content.Split('\n').Select(line =>
                line.Length == 0 ? string.Empty : indentation + line));
    }

    private static string Normalize(string content)
    {
        var builder = new StringBuilder(content.Length + 1);
        builder.Append(content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\r', '\n'));
        builder.Append('\n');
        return builder.ToString();
    }
}

/// <summary>
/// 表示目录迁移草案生成结果。
/// </summary>
/// <param name="SqlServerDraft">SQL Server 建表草案文本。</param>
/// <param name="MySqlDraft">MySQL 建表草案文本。</param>
/// <param name="Warnings">跨方言映射或省略列时的警告。</param>
public sealed record CatalogMigrationDraftResult(
    string SqlServerDraft,
    string MySqlDraft,
    IReadOnlyList<string> Warnings);
