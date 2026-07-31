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
            ? "\n"
                + IndentLines(
                    $$"""
                    CREATE CLUSTERED INDEX {{TenantIndexName(schema)}}
                        ON dbo.{{schema.DatabaseTableName}}(TenantId, Id);
                    """,
                    4)
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
                );{{tenantIndex}}
            END;
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
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
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
                $"{column.DatabaseName} {RenderType(column, provider)} "
                + (column.IsNullable ? "NULL" : "NOT NULL")));

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
