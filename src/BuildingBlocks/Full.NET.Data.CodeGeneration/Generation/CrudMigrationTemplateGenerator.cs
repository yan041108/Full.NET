using System.Text;
using Full.NET.Data.CodeGeneration.Naming;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 生成不会被 DbUp 自动发现的双库迁移与集成测试草案，避免猜测迁移序号或静默采用不完整数据库语义。
/// </summary>
internal static class CrudMigrationTemplateGenerator
{
    /// <summary>生成 SQL Server 建表草案。</summary>
    internal static string GenerateSqlServer(FullNetCrudSchema schema)
    {
        EnsureExplicitScope(schema);
        var primaryKeyName = DatabaseObjectNameBuilder.Build(
            $"PK_{schema.DatabaseTableName}");
        var primaryKeyKind = schema.IsTenantScoped
            ? "NONCLUSTERED"
            : "CLUSTERED";
        var tenantIndex = schema.IsTenantScoped
            ? "\n" + $$"""
                -- 建表已完成但索引未完成时，未记账重跑仍需独立收敛。
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.{{schema.DatabaseTableName}}', N'U')
                      AND name = N'{{TenantIndexName(schema)}}'
                )
                BEGIN
                    CREATE CLUSTERED INDEX {{TenantIndexName(schema)}}
                        ON dbo.{{schema.DatabaseTableName}}(TenantId, Id);
                END;
                """
            : string.Empty;

        return Normalize(
            $$""""
            -- Full.NET 生成的 SQL Server 迁移草案。
            -- 请分配真实迁移编号并完成幂等恢复评审后，再移入正式 DbUp 目录。
            IF OBJECT_ID(N'dbo.{{schema.DatabaseTableName}}', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.{{schema.DatabaseTableName}}
                (
            {{IndentLines(RenderColumns(schema, DatabaseMetadataProvider.SqlServer), 8)}},
                    CONSTRAINT {{primaryKeyName}} PRIMARY KEY {{primaryKeyKind}} (Id)
                );
            END;{{tenantIndex}}{{GenerateUniqueIndexes(schema, DatabaseMetadataProvider.SqlServer)}}{{CrudTreeGenerator.Migration(schema, false)}}{{(CrudAggregateGenerator.Enabled(schema) ? GenerateSqlServer(CrudAggregateGenerator.Detail(schema)) : string.Empty)}}
            """");
    }

    /// <summary>生成 MySQL 建表草案。</summary>
    internal static string GenerateMySql(FullNetCrudSchema schema)
    {
        EnsureExplicitScope(schema);
        var primaryKeyName = DatabaseObjectNameBuilder.Build(
            $"PK_{schema.DatabaseTableName}");
        var tenantIndex = schema.IsTenantScoped
            ? $",\n    KEY {TenantIndexName(schema)} (TenantId, Id)"
            : string.Empty;

        return Normalize(
            $$"""
            -- Full.NET 生成的 MySQL 迁移草案。
            -- 请分配真实迁移编号并完成幂等恢复评审后，再移入正式 DbUp 目录。
            CREATE TABLE IF NOT EXISTS {{schema.DatabaseTableName}}
            (
            {{IndentLines(RenderColumns(schema, DatabaseMetadataProvider.MySql), 4)}},
                CONSTRAINT {{primaryKeyName}} PRIMARY KEY (Id){{tenantIndex}}
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;{{GenerateUniqueIndexes(schema, DatabaseMetadataProvider.MySql)}}{{CrudTreeGenerator.Migration(schema, true)}}{{(CrudAggregateGenerator.Enabled(schema) ? GenerateMySql(CrudAggregateGenerator.Detail(schema)) : string.Empty)}}
            """);
    }

    /// <summary>生成采用现有共享数据库夹具的最小双 Provider 集成测试草案。</summary>
    internal static string GenerateIntegrationTest(FullNetCrudSchema schema)
    {
        EnsureExplicitScope(schema);

        return Normalize(
            $$""""
            #nullable enable

            using Dapper;
            using Full.NET.IntegrationTests;
            using Microsoft.Data.SqlClient;
            using Microsoft.VisualStudio.TestTools.UnitTesting;
            using MySqlConnector;

            namespace {{schema.RootNamespace}}.Generated.Tests;

            // 采用说明：先为成对 SQL 草案分配同一迁移编号并移入正式目录，再移除 .template 后缀。
            [TestClass]
            public sealed class {{schema.ClrTypeName}}MigrationIntegrationTests
            {
                [TestMethod]
                public async Task SqlServer_migration_exposes_the_expected_table_shape()
                {
                    var connectionString =
                        await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
                    await using var connection = new SqlConnection(connectionString);
                    var columnCount = await connection.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(*)
                        FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = 'dbo'
                          AND TABLE_NAME = '{{schema.DatabaseTableName}}'
                        """);

                    Assert.AreEqual({{schema.Columns.Count}}, columnCount);
                }

                [TestMethod]
                public async Task MySql_migration_exposes_the_expected_table_shape()
                {
                    var connectionString =
                        await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
                    await using var connection = new MySqlConnection(connectionString);
                    var columnCount = await connection.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(*)
                        FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = '{{schema.DatabaseTableName}}'
                        """);

                    Assert.AreEqual({{schema.Columns.Count}}, columnCount);
                }
            }
            """");
    }

    private static string RenderColumns(
        FullNetCrudSchema schema,
        DatabaseMetadataProvider provider) =>
        string.Join(
            ",\n",
            schema.Columns.Select(column =>
                $"{column.DatabaseName} {RenderType(column, provider)}{UniqueCollation(schema, column, provider)} "
                + (column.IsNullable ? "NULL" : "NOT NULL")));

    // 唯一文本显式使用区分大小写的二进制排序，避免由两个数据库的默认排序规则决定业务。
    private static string UniqueCollation(FullNetCrudSchema schema, FullNetColumn column, DatabaseMetadataProvider provider) =>
        column.ScalarType == FullNetScalarType.String && CrudUniqueKeyGenerator.Keys(schema).Any(key => key.Contains(column.DatabaseName))
            ? provider == DatabaseMetadataProvider.SqlServer ? " COLLATE Latin1_General_100_BIN2" : " COLLATE utf8mb4_bin"
            : string.Empty;

    private static string RenderType(
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
                "不支持的迁移模板字段类型。"),
        };

    private static string TenantIndexName(FullNetCrudSchema schema) =>
        DatabaseObjectNameBuilder.Build(
            $"IX_{schema.DatabaseTableName}_TenantId_Id");

    // 索引独立探测，覆盖表已创建但 DbUp 尚未记账的重跑；NULL 不参与唯一性。
    private static string GenerateUniqueIndexes(FullNetCrudSchema schema, DatabaseMetadataProvider provider) =>
        string.Concat(CrudUniqueKeyGenerator.Keys(schema).Select(key =>
        {
            var name = CrudUniqueKeyGenerator.Name(schema, key);
            var columns = string.Join(", ", key);
            var textColumns = key.Where(column => schema.Columns.Single(item => item.DatabaseName == column).ScalarType == FullNetScalarType.String).ToArray();
            if (provider == DatabaseMetadataProvider.SqlServer)
            {
                var nullable = key.Where(column => schema.Columns.Single(item => item.DatabaseName == column).IsNullable).ToArray();
                var filter = nullable.Length == 0 ? string.Empty
                    : " WHERE " + string.Join(" AND ", nullable.Select(column => column + " IS NOT NULL"));
                var collationGuard = textColumns.Length == 0 ? string.Empty : $$"""

                    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.{{schema.DatabaseTableName}}', N'U')
                        AND name IN ({{string.Join(", ", textColumns.Select(column => "N'" + column + "'"))}}) AND collation_name <> N'Latin1_General_100_BIN2')
                        THROW 51000, 'Generated unique key collation mismatch.', 1;
                    """;
                return collationGuard + $$"""

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                        WHERE object_id = OBJECT_ID(N'dbo.{{schema.DatabaseTableName}}', N'U') AND name = N'{{name}}'
                          AND is_unique = 1 AND is_disabled = 0 AND ignore_dup_key = 0
                          AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id = sys.indexes.object_id
                               AND c.index_id = sys.indexes.index_id AND c.key_ordinal > 0) = {{key.Count}}
                          {{string.Join("\n                          ", key.Select((column, index) => $"AND EXISTS (SELECT 1 FROM sys.index_columns c INNER JOIN sys.columns v ON v.object_id = c.object_id AND v.column_id = c.column_id WHERE c.object_id = sys.indexes.object_id AND c.index_id = sys.indexes.index_id AND c.key_ordinal = {index + 1} AND v.name = N'{column}')"))}}
                          AND {{(nullable.Length == 0 ? "filter_definition IS NULL" : "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(filter_definition, '[', ''), ']', ''), '(', ''), ')', ''), ' ', '') = N'" + string.Join("AND", nullable.Select(column => column + "ISNOTNULL")) + "'")}})
                    BEGIN
                        CREATE UNIQUE NONCLUSTERED INDEX {{name}} ON dbo.{{schema.DatabaseTableName}} ({{columns}}){{filter}};
                    END;
                    """;
            }

            var mysqlGuard = textColumns.Length == 0 ? string.Empty : $$"""

                -- 标量子查询在排序规则漂移时返回两行并失败，禁止沿用提供程序默认唯一语义。
                SET @fullnet_codegen_shape_guard = (SELECT Value FROM (
                    SELECT 1 AS Value UNION ALL SELECT 1 WHERE EXISTS (
                        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = '{{schema.DatabaseTableName}}'
                          AND COLUMN_NAME IN ({{string.Join(", ", textColumns.Select(column => "'" + column + "'"))}})
                          AND COLLATION_NAME <> 'utf8mb4_bin'
                    )
                ) AS fullnet_codegen_shape);
                """;
            return mysqlGuard + $$"""

                SET @fullnet_codegen_index_sql = IF(EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{{schema.DatabaseTableName}}' AND INDEX_NAME = '{{name}}'
                    GROUP BY INDEX_NAME HAVING MIN(NON_UNIQUE) = 0
                        AND GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ',') = '{{string.Join(",", key)}}'
                        AND SUM(SUB_PART IS NOT NULL) = 0
                ), 'SELECT 1', 'CREATE UNIQUE INDEX {{name}} ON {{schema.DatabaseTableName}} ({{columns}})');
                PREPARE fullnet_codegen_index_statement FROM @fullnet_codegen_index_sql;
                EXECUTE fullnet_codegen_index_statement;
                DEALLOCATE PREPARE fullnet_codegen_index_statement;
                """;
        }));

    private static void EnsureExplicitScope(FullNetCrudSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.DataScope == FullNetCrudDataScope.Unspecified)
        {
            throw new ArgumentException(
                "迁移模板只接受数据作用域明确的 CRUD Schema。",
                nameof(schema));
        }
    }

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
