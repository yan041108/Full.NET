using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 039 能从流水号关键唯一索引缺失或形状错误的状态收敛。</summary>
[TestClass]
public sealed class Migration039SerialNumbersRecoveryTests
{
    [TestMethod]
    public async Task MySql_serial_number_migration_recovers_missing_or_malformed_unique_indexes()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await connection.ExecuteAsync(
            """
            DROP INDEX UX_fn_serialnumbers_counter_ScopeBucket
                ON fn_serialnumbers_counter;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%039_SerialNumbers.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertMySqlIndexAsync(
            connection,
            "fn_serialnumbers_counter",
            "UX_fn_serialnumbers_counter_ScopeBucket",
            ["ScopeTenantKey", "RuleId", "ResetBucket"]);

        await connection.ExecuteAsync(
            """
            DROP INDEX UX_fn_serialnumbers_allocation_Idempotency
                ON fn_serialnumbers_allocation;
            CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_Idempotency
                ON fn_serialnumbers_allocation(RuleId, IdempotencyKey);
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%039_SerialNumbers.sql';
            """);
        recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertMySqlIndexAsync(
            connection,
            "fn_serialnumbers_allocation",
            "UX_fn_serialnumbers_allocation_Idempotency",
            ["ScopeTenantKey", "RuleId", "IdempotencyKey"]);
    }

    [TestMethod]
    public async Task SqlServer_serial_number_migration_recovers_missing_or_malformed_unique_indexes()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(
            DatabaseProvider.SqlServer,
            connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            """
            DROP INDEX UX_fn_serialnumbers_counter_HostBucket
                ON dbo.fn_serialnumbers_counter;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%039_SerialNumbers.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertSqlServerIndexAsync(
            connection,
            "fn_serialnumbers_counter",
            "UX_fn_serialnumbers_counter_HostBucket",
            ["RuleId", "ResetBucket"],
            "([TenantId] IS NULL)");

        await connection.ExecuteAsync(
            """
            DROP INDEX UX_fn_serialnumbers_allocation_TenantIdempotency
                ON dbo.fn_serialnumbers_allocation;
            CREATE UNIQUE INDEX
                UX_fn_serialnumbers_allocation_TenantIdempotency
                ON dbo.fn_serialnumbers_allocation(
                    RuleId, TenantId, IdempotencyKey);
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%039_SerialNumbers.sql';
            """);
        recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await AssertSqlServerIndexAsync(
            connection,
            "fn_serialnumbers_allocation",
            "UX_fn_serialnumbers_allocation_TenantIdempotency",
            ["TenantId", "RuleId", "IdempotencyKey"],
            "([TenantId] IS NOT NULL)");
    }

    private static async Task AssertMySqlIndexAsync(
        MySqlConnection connection,
        string tableName,
        string indexName,
        IReadOnlyList<string> columns)
    {
        var rows = (await connection.QueryAsync<MySqlIndexRow>(
            """
            SELECT COLUMN_NAME AS ColumnName,
                   SEQ_IN_INDEX AS Sequence,
                   NON_UNIQUE AS NonUnique,
                   SUB_PART AS PrefixLength
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @TableName
              AND INDEX_NAME = @IndexName
            ORDER BY SEQ_IN_INDEX
            """,
            new { TableName = tableName, IndexName = indexName }))
            .ToArray();
        Assert.HasCount(columns.Count, rows);
        CollectionAssert.AreEqual(
            columns.ToArray(),
            rows.Select(row => row.ColumnName).ToArray());
        Assert.IsTrue(rows.All(row =>
            row.NonUnique == 0 && row.PrefixLength is null));
    }

    private static async Task AssertSqlServerIndexAsync(
        SqlConnection connection,
        string tableName,
        string indexName,
        IReadOnlyList<string> columns,
        string filterDefinition)
    {
        var rows = (await connection.QueryAsync<SqlServerIndexRow>(
            """
            SELECT columnObject.name AS ColumnName,
                   indexColumn.key_ordinal AS Sequence,
                   indexObject.is_unique AS IsUnique,
                   indexObject.is_disabled AS IsDisabled,
                   indexObject.filter_definition AS FilterDefinition
            FROM sys.indexes AS indexObject
            INNER JOIN sys.index_columns AS indexColumn
                ON indexColumn.object_id = indexObject.object_id
               AND indexColumn.index_id = indexObject.index_id
               AND indexColumn.key_ordinal > 0
            INNER JOIN sys.columns AS columnObject
                ON columnObject.object_id = indexColumn.object_id
               AND columnObject.column_id = indexColumn.column_id
            WHERE indexObject.object_id =
                  OBJECT_ID(N'dbo.' + @TableName)
              AND indexObject.name = @IndexName
            ORDER BY indexColumn.key_ordinal
            """,
            new { TableName = tableName, IndexName = indexName }))
            .ToArray();
        Assert.HasCount(columns.Count, rows);
        CollectionAssert.AreEqual(
            columns.ToArray(),
            rows.Select(row => row.ColumnName).ToArray());
        Assert.IsTrue(rows.All(row => row.IsUnique && !row.IsDisabled));
        Assert.IsTrue(rows.All(row =>
            string.Equals(
                filterDefinition,
                row.FilterDefinition,
                StringComparison.OrdinalIgnoreCase)));
    }

    private static DbUpMigrationRunner CreateRunner(
        DatabaseProvider provider,
        string connectionString) =>
        new(
            Options.Create(new DatabaseOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                CommandTimeoutSeconds = 300,
            }),
            NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions());

    private sealed class MySqlIndexRow
    {
        public string ColumnName { get; set; } = string.Empty;

        public int Sequence { get; set; }

        public int NonUnique { get; set; }

        public int? PrefixLength { get; set; }
    }

    private sealed class SqlServerIndexRow
    {
        public string ColumnName { get; set; } = string.Empty;

        public int Sequence { get; set; }

        public bool IsUnique { get; set; }

        public bool IsDisabled { get; set; }

        public string FilterDefinition { get; set; } = string.Empty;
    }
}
