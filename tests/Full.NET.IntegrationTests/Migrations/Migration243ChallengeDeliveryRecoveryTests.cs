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

/// <summary>验证挑战投递状态的旧记录兼容、半完成 DDL 与未记账重放恢复。</summary>
[TestClass]
public sealed class Migration243ChallengeDeliveryRecoveryTests
{
    /// <summary>SQL Server 分步增量列与元数据必须重入收敛。</summary>
    [TestMethod]
    public Task SqlServer_recovers_partial_metadata_and_replays_without_duplicate_objects() => VerifyAsync(true);

    /// <summary>MySQL 已提交部分列后必须继续恢复并保留旧记录。</summary>
    [TestMethod]
    public Task MySql_recovers_partial_metadata_and_replays_without_duplicate_objects() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var provider = sqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.MySql;
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await MigrateThrough242Async(provider, connectionString);
        await using DbConnection connection = sqlServer ? new SqlConnection(connectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        var id = Guid.CreateVersion7();
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        await connection.ExecuteAsync("""
            INSERT INTO fn_identity_account_challenge
                (ChallengeId, Purpose, NormalizedEmail, CredentialHash, ExpiresAtUtc,
                 ConsumedAtUtc, AttemptCount, MaxAttempts, Version, CreatedAtUtc)
            VALUES (@Id, 1, 'legacy@example.test', @Hash, @Expires, NULL, 0, 5, 1, @Now)
            """, new { Id = id, Hash = new string('a', 64), Expires = expires, Now = DateTimeOffset.UtcNow });
        // 模拟首列已经提交而其余列未执行；MySQL COMMENT 与 ADD COLUMN 是同一个原子 DDL 步骤。
        await connection.ExecuteAsync(sqlServer
            ? "ALTER TABLE dbo.fn_identity_account_challenge ADD DeliveryStateKey varchar(16) NULL"
            : "ALTER TABLE fn_identity_account_challenge ADD COLUMN DeliveryStateKey VARCHAR(16) NULL COMMENT '投递结果机器码；空值仅表示迁移前旧挑战'");
        void Replay243()
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString,
                    MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                        && name.EndsWith("243_IdentityChallengeDeliveryJournal.sql", StringComparison.Ordinal))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString());
            Assert.HasCount(1, result.Scripts);
        }
        Replay243();
        // 删除后两列再重放，验证半完成恢复不是 DbUp 已记账后的零脚本重跑。
        await connection.ExecuteAsync("ALTER TABLE fn_identity_account_challenge DROP COLUMN DeliveryCompletedAtUtc");
        await connection.ExecuteAsync("ALTER TABLE fn_identity_account_challenge DROP COLUMN DeliveryReconciledAtUtc");
        Replay243();
        Replay243();
        foreach (var name in new[] { "DeliveryStateKey", "DeliveryCompletedAtUtc", "DeliveryReconciledAtUtc" })
        {
            var count = await connection.ExecuteScalarAsync<int>(sqlServer
                ? "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_identity_account_challenge') AND name = @Name"
                : "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_account_challenge' AND COLUMN_NAME = @Name", new { Name = name });
            Assert.AreEqual(1, count);
            var comment = await connection.ExecuteScalarAsync<string>(sqlServer
                ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), @Name, 'ColumnId') AND name = N'MS_Description'"
                : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_account_challenge' AND COLUMN_NAME = @Name", new { Name = name });
            Assert.IsFalse(string.IsNullOrWhiteSpace(comment));
        }
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM fn_identity_account_challenge
            WHERE ChallengeId = @Id AND DeliveryStateKey IS NULL
              AND DeliveryCompletedAtUtc IS NULL AND DeliveryReconciledAtUtc IS NULL
              AND ConsumedAtUtc IS NULL AND Version = 1 AND CredentialHash = @Hash
            """, new { Id = id, Hash = new string('a', 64) }));
        // 数据库日期精度差异不允许迁移延长旧凭据的有效窗口。
        var expirySql = "SELECT ExpiresAtUtc FROM fn_identity_account_challenge WHERE ChallengeId = @Id";
        var storedExpiry = sqlServer ? await connection.ExecuteScalarAsync<DateTimeOffset>(expirySql, new { Id = id })
            : new DateTimeOffset(DateTime.SpecifyKind(await connection.ExecuteScalarAsync<DateTime>(expirySql, new { Id = id }), DateTimeKind.Utc));
        Assert.IsTrue(Math.Abs((storedExpiry - expires).TotalMilliseconds) < 1);
    }

    /// <summary>使用正式 Runner 的清单闭包冻结旧结构至 242，后续新增迁移不进入恢复夹具。</summary>
    private static async Task MigrateThrough242Async(DatabaseProvider provider, string connectionString)
    {
        var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
        var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(segment, StringComparison.Ordinal)
                && NamingExpandTestMigrationRunner.IsThroughMigration(name, 242)).ToArray();
        Assert.IsTrue(names.Any(name => name.EndsWith("242_AiKnowledgeDocument.sql", StringComparison.Ordinal)));
        Assert.IsFalse(names.Any(name => name.Contains("243_", StringComparison.Ordinal)));
        var workspace = Directory.CreateTempSubdirectory("fullnet-through242-").FullName;
        try
        {
            // 资源名尾部包含 .sql，按 Provider 分段取完整文件名。
            var scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] });
            File.WriteAllText(Path.Combine(workspace, "framework-manifest.json"), JsonSerializer.Serialize(new
            {
                migrationInventory = new { selectionStatus = "preset-recovery-through242", scripts },
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
