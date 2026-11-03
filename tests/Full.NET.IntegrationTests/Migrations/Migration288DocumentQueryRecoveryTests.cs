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

/// <summary>288 的未记账部分 DDL 恢复；只装配冻结至 287 的历史基线，不运行其他迁移场景。</summary>
[TestClass]
public sealed class Migration288DocumentQueryRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Partial_event_column_recovers_guard_seed_and_unique_index(DatabaseProvider provider)
    {
        var sqlServer = provider == DatabaseProvider.SqlServer;
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await MigrateThrough287Async(provider, connectionString);
        await using DbConnection connection = sqlServer ? new SqlConnection(connectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.ExecuteAsync(sqlServer
            ? "ALTER TABLE fn_document_access_log ADD OperationKey varchar(192) NULL"
            : "ALTER TABLE fn_document_access_log ADD OperationKey varchar(192) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '逻辑访问身份，重试与 Range 请求归并'");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString,
                    MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                        && name.EndsWith("288_DocumentQueryGovernance.sql", StringComparison.Ordinal))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
        }
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_document_category_guard"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_document_access_log') AND name='UX_fn_document_access_log_OperationKey'"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_document_access_log' AND INDEX_NAME='UX_fn_document_access_log_OperationKey'"));
    }

    private static async Task MigrateThrough287Async(DatabaseProvider provider, string connectionString)
    {
        var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
        var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(segment, StringComparison.Ordinal)
                && NamingExpandTestMigrationRunner.IsThroughMigration(name, 287)).ToArray();
        var workspace = Directory.CreateTempSubdirectory("fullnet-document-through287-").FullName;
        try
        {
            var scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] });
            File.WriteAllText(Path.Combine(workspace, "framework-manifest.json"), JsonSerializer.Serialize(new
            {
                migrationInventory = new { selectionStatus = "preset-document-through287", scripts },
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
