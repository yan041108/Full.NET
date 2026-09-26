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

/// <summary>验证恢复码表的未记账重跑及索引、对象说明半完成恢复。</summary>
[TestClass]
public sealed class Migration238MfaRecoveryCodeRecoveryTests
{
    /// <summary>SQL Server 已有表在索引或说明缺失后必须收敛。</summary>
    [TestMethod]
    public Task SqlServer_recovers_partial_metadata_and_replays_without_duplicate_objects() => VerifyAsync(true);

    /// <summary>MySQL 已提交建表后重跑不得因重复索引而失败。</summary>
    [TestMethod]
    public Task MySql_recovers_partial_metadata_and_replays_without_duplicate_objects() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var provider = sqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.MySql;
        var connectionString = sqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await MigrateThrough237Async(provider, connectionString);
        await using DbConnection connection = sqlServer
            ? new SqlConnection(connectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);

        // 精确限定目标脚本且不记账，确保验证实际 DDL 重入而非 DbUp 零脚本重跑。
        void Replay238()
        {
            var builder = sqlServer
                ? DeployChanges.To.SqlDatabase(connectionString)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(
                    connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                        && name.EndsWith("238_IdentityMfaRecoveryCode.sql", StringComparison.Ordinal))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300))
                .Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString());
            Assert.HasCount(1, result.Scripts);
        }

        Replay238();
        if (!sqlServer)
        {
            // InnoDB 可能删除原隐式外键索引；保留独立支撑索引后再模拟目标索引缺失。
            await connection.ExecuteAsync("CREATE INDEX IX_probe_recovery_code_UserId ON fn_identity_user_mfa_recovery_code(UserId)");
        }
        await connection.ExecuteAsync(sqlServer
            ? "DROP INDEX IX_fn_identity_user_mfa_recovery_code_UserId ON dbo.fn_identity_user_mfa_recovery_code"
            : "ALTER TABLE fn_identity_user_mfa_recovery_code DROP INDEX IX_fn_identity_user_mfa_recovery_code_UserId");
        if (sqlServer)
        {
            await connection.ExecuteAsync("""
                EXEC sys.sp_dropextendedproperty @name=N'MS_Description',
                    @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE', @level1name=N'fn_identity_user_mfa_recovery_code',
                    @level2type=N'COLUMN', @level2name=N'CodeHash'
                """);
        }
        Replay238();
        Replay238();
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(connection,
            "fn_identity_user_mfa_recovery_code", "IX_fn_identity_user_mfa_recovery_code_UserId", sqlServer));
        var description = await connection.ExecuteScalarAsync<string>(sqlServer
            ? """
                SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties
                WHERE major_id = OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code')
                    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code'), N'CodeHash', 'ColumnId')
                    AND name = N'MS_Description'
                """
            : """
                SELECT COLUMN_COMMENT FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_user_mfa_recovery_code' AND COLUMN_NAME = 'CodeHash'
                """);
        StringAssert.Contains(description, "恢复码");
    }

    /// <summary>使用正式 Runner 的清单闭包冻结旧结构至 237，后续新增迁移不进入恢复夹具。</summary>
    private static async Task MigrateThrough237Async(DatabaseProvider provider, string connectionString)
    {
        var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
        var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(segment, StringComparison.Ordinal)
                && NamingExpandTestMigrationRunner.IsThroughMigration(name, 237)).ToArray();
        Assert.IsTrue(names.Any(name => name.EndsWith("237_ReportingDataSourceTestMessageRedaction.sql", StringComparison.Ordinal)));
        Assert.IsFalse(names.Any(name => name.Contains("238_", StringComparison.Ordinal)));
        var workspace = Directory.CreateTempSubdirectory("fullnet-through237-").FullName;
        try
        {
            // 资源名尾部包含 .sql，按 Provider 分段取完整文件名。
            var scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] });
            File.WriteAllText(Path.Combine(workspace, "framework-manifest.json"), JsonSerializer.Serialize(new
            {
                migrationInventory = new { selectionStatus = "preset-recovery-through237", scripts },
            }));
            var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions
                {
                    Provider = provider, ConnectionString = connectionString,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300,
                }), NullLoggerFactory.Instance,
                MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = workspace }));
            Assert.AreEqual(names.Length, (await runner.MigrateAsync()).ExecutedScriptCount);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
