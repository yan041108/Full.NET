using Dapper;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// 在同一 Testcontainers 实例上将 Through009 库的业务行逻辑克隆到目标库。
/// 目标库先跑 001–009 以获得完整 schema 与 Journal，再按外键顺序覆盖数据；
/// 不复制 SchemaVersions，也不依赖 BACKUP/mysqldump 文件介质。
/// </summary>
internal static class DatabaseLogicalClone
{
    /// <summary>
    /// 父表在前、子表在后，保证 INSERT 不违反 Through009 的 6 条外键。
    /// </summary>
    private static readonly string[] CopyOrder =
    [
        "fn_uuid_contract_state",
        "fn_tenant_tenant",
        "fn_outbox_message",
        "fn_identity_user",
        "fn_identity_role",
        "fn_seed_run",
        "fn_identity_refresh_session",
        "fn_identity_auth_audit",
        "fn_identity_user_role",
        "fn_identity_role_permission",
        "fn_seed_run_item",
    ];

    /// <summary>
    /// 将 SQL Server 源库（已 Through009）的业务数据克隆到新目标库，Journal 停在 009。
    /// </summary>
    /// <returns>指向已克隆目标库的连接串。</returns>
    public static async Task<string> CloneSqlServerThrough009Async(string sourceConnectionString)
    {
        var targetConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough009Async(
            targetConnectionString);

        var sourceDb = QuoteSqlServerIdent(
            new SqlConnectionStringBuilder(sourceConnectionString).InitialCatalog);
        var targetDb = QuoteSqlServerIdent(
            new SqlConnectionStringBuilder(targetConnectionString).InitialCatalog);

        // 使用 master 连接做跨库复制，避免目标库会话上下文干扰三方名称解析。
        var masterConnectionString = new SqlConnectionStringBuilder(targetConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        foreach (var table in CopyOrder.Reverse())
        {
            await connection.ExecuteAsync(
                $"DELETE FROM {targetDb}.dbo.[{table}];",
                transaction: transaction);
        }

        foreach (var table in CopyOrder)
        {
            await connection.ExecuteAsync(
                $"""
                INSERT INTO {targetDb}.dbo.[{table}]
                SELECT * FROM {sourceDb}.dbo.[{table}];
                """,
                transaction: transaction);
        }

        var tenantRows = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {targetDb}.dbo.[fn_tenant_tenant];",
            transaction: transaction);
        var outboxRows = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {targetDb}.dbo.[fn_outbox_message];",
            transaction: transaction);
        if (tenantRows < 1 || outboxRows < 1)
        {
            throw new InvalidOperationException(
                $"SQL Server logical clone incomplete: tenants={tenantRows}, outbox={outboxRows}.");
        }

        await transaction.CommitAsync();
        return targetConnectionString;
    }

    /// <summary>
    /// 将 MySQL 源库（已 Through009）的业务数据克隆到新目标库，Journal 停在 009。
    /// </summary>
    /// <returns>指向已克隆目标库的连接串。</returns>
    public static async Task<string> CloneMySqlThrough009Async(string sourceConnectionString)
    {
        var targetConnectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough009Async(
            targetConnectionString);

        var sourceDb = new MySqlConnectionStringBuilder(sourceConnectionString).Database;
        var targetDb = new MySqlConnectionStringBuilder(targetConnectionString).Database;
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                targetConnectionString,
                Full.NET.Data.Abstractions.MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var table in CopyOrder.Reverse())
        {
            await connection.ExecuteAsync(
                $"DELETE FROM `{targetDb}`.`{table}`;",
                transaction: transaction);
        }

        foreach (var table in CopyOrder)
        {
            await connection.ExecuteAsync(
                $"""
                INSERT INTO `{targetDb}`.`{table}`
                SELECT * FROM `{sourceDb}`.`{table}`;
                """,
                transaction: transaction);
        }

        var tenantRows = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM `{targetDb}`.`fn_tenant_tenant`;",
            transaction: transaction);
        var outboxRows = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM `{targetDb}`.`fn_outbox_message`;",
            transaction: transaction);
        if (tenantRows < 1 || outboxRows < 1)
        {
            throw new InvalidOperationException(
                $"MySQL logical clone incomplete: tenants={tenantRows}, outbox={outboxRows}.");
        }

        await transaction.CommitAsync();
        return targetConnectionString;
    }

    private static string QuoteSqlServerIdent(string name) =>
        "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
