using System.Data.Common;
using System.Text.Json;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证所有者迁移可重复执行并恢复未记账的首列，不编造历史关联数据。</summary>
[TestClass]
public sealed class Migration262LogCorrelationRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_partial_correlation_columns() => VerifyAsync(true);
    [TestMethod]
    public Task MySql_recovers_partial_correlation_columns() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var provider = sqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.MySql;
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await MigrateThrough261Async(provider, connectionString);
        await using DbConnection connection = sqlServer ? new SqlConnection(connectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        var idExpression = sqlServer ? "NEWID()" : "UNHEX(REPLACE(UUID(), '-', ''))";
        await connection.ExecuteAsync($"""
            INSERT INTO fn_auditing_operation_log
                (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode, DurationMs, Succeeded)
            VALUES ({idExpression}, @Time, 'legacy', 'GET', '/legacy', 200, 1, 1)
            """, new { Time = DateTimeOffset.UtcNow });
        await connection.ExecuteAsync(sqlServer
            ? "ALTER TABLE fn_auditing_access_log ADD CorrelationJson nvarchar(2048) NULL"
            : "ALTER TABLE fn_auditing_access_log ADD COLUMN CorrelationJson varchar(2048) NULL");
        for (var run = 0; run < 2; run++)
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString,
                    MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                        && name.EndsWith("LogCorrelation.sql", StringComparison.Ordinal))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString());
            Assert.HasCount(10, result.Scripts);
        }
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_auditing_operation_log WHERE ActionKey='legacy' AND CorrelationJson IS NULL"));
        Assert.AreEqual(16, await connection.ExecuteScalarAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME='CorrelationJson' AND TABLE_SCHEMA='dbo'"
            : "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE COLUMN_NAME='CorrelationJson' AND TABLE_SCHEMA=DATABASE()"));
        var comment = await connection.ExecuteScalarAsync<string>(sqlServer
            ? """
              SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties
              WHERE major_id=OBJECT_ID(N'dbo.fn_auditing_access_log')
                AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_access_log'), N'CorrelationJson', 'ColumnId')
                AND name=N'MS_Description'
              """
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_auditing_access_log' AND COLUMN_NAME='CorrelationJson'");
        Assert.AreEqual("事件产生时的关联快照；历史缺失为空，不含凭据或原始IP", comment);
    }

    private static async Task MigrateThrough261Async(DatabaseProvider provider, string connectionString)
    {
        var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
        var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(segment, StringComparison.Ordinal)
                && NamingExpandTestMigrationRunner.IsThroughMigration(name, 261)).ToArray();
        var workspace = Directory.CreateTempSubdirectory("fullnet-correlation-through261-").FullName;
        try
        {
            var scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] });
            File.WriteAllText(Path.Combine(workspace, "framework-manifest.json"), JsonSerializer.Serialize(new
            {
                migrationInventory = new { selectionStatus = "preset-log-correlation-through261", scripts },
            }));
            var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions
                {
                    Provider = provider, ConnectionString = connectionString,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300,
                }), NullLoggerFactory.Instance,
                MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = workspace }));
            var result = await runner.MigrateAsync();
            Assert.IsTrue(result.Successful);
            Assert.AreEqual(names.Length, result.ExecutedScriptCount);
        }
        finally
        {
            // 仅删除本测试刚创建且路径已验证的临时目录。
            Assert.IsTrue(Path.GetFullPath(workspace).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase));
            Directory.Delete(workspace, recursive: true);
        }
    }
}
