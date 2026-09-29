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

/// <summary>验证操作详情可空扩展的旧记录兼容与未记账半完成恢复。</summary>
[TestClass]
public sealed class Migration239AuditingOperationLogContextRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_partial_context_column_and_preserves_legacy_nulls() => VerifyAsync(true);

    [TestMethod]
    public Task MySql_recovers_partial_context_column_and_preserves_legacy_nulls() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var provider = sqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.MySql;
        var connectionString = sqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await MigrateThrough238Async(provider, connectionString);
        await using DbConnection connection = sqlServer
            ? new SqlConnection(connectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        var legacyId = Guid.CreateVersion7();
        var idHex = Convert.ToHexString(legacyId.ToByteArray(bigEndian: true));
        await connection.ExecuteAsync(sqlServer
            ? """
              INSERT INTO dbo.fn_auditing_operation_log
                  (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode,
                   DurationMs, Succeeded, UserId, TenantId, TraceId, ClientIpFingerprint, PermissionCode)
              VALUES
                  (@Id, @OccurredAtUtc, 'legacy.operation', 'POST', '/legacy', 200,
                   1, 1, NULL, NULL, NULL, NULL, NULL)
              """
            : """
              INSERT INTO fn_auditing_operation_log
                  (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode,
                   DurationMs, Succeeded, UserId, TenantId, TraceId, ClientIpFingerprint, PermissionCode)
              VALUES
                  (UNHEX(@IdHex), @OccurredAtUtc, 'legacy.operation', 'POST', '/legacy', 200,
                   1, 1, NULL, NULL, NULL, NULL, NULL)
              """,
            new { Id = legacyId, IdHex = idHex, OccurredAtUtc = DateTimeOffset.UtcNow });

        // 先模拟第一列 DDL 已提交但 DbUp 尚未记账，重放必须补齐第二列和 SQL Server 元数据。
        await connection.ExecuteAsync(sqlServer
            ? "ALTER TABLE dbo.fn_auditing_operation_log ADD ContextJson nvarchar(max) NULL"
            : "ALTER TABLE fn_auditing_operation_log ADD COLUMN ContextJson TEXT NULL COMMENT '版本化且受限的操作详情 JSON'");
        Replay239(sqlServer, connectionString);
        Replay239(sqlServer, connectionString);

        var legacyNullCount = await connection.ExecuteScalarAsync<int>(sqlServer
            ? """
              SELECT COUNT(*) FROM dbo.fn_auditing_operation_log
              WHERE Id = @Id AND ContextJson IS NULL AND DetailsExpiresAtUtc IS NULL
              """
            : """
              SELECT COUNT(*) FROM fn_auditing_operation_log
              WHERE Id = UNHEX(@IdHex) AND ContextJson IS NULL AND DetailsExpiresAtUtc IS NULL
              """,
            new { Id = legacyId, IdHex = idHex });
        Assert.AreEqual(1, legacyNullCount);

        var contextComment = await connection.ExecuteScalarAsync<string>(sqlServer
            ? """
              SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties
              WHERE major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
                AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'ContextJson', 'ColumnId')
                AND name = N'MS_Description'
              """
            : """
              SELECT COLUMN_COMMENT FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE()
                AND TABLE_NAME = 'fn_auditing_operation_log'
                AND COLUMN_NAME = 'ContextJson'
              """);
        Assert.AreEqual("版本化且受限的操作详情 JSON", contextComment);

        var expiryComment = await connection.ExecuteScalarAsync<string>(sqlServer
            ? """
              SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties
              WHERE major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
                AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'DetailsExpiresAtUtc', 'ColumnId')
                AND name = N'MS_Description'
              """
            : """
              SELECT COLUMN_COMMENT FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE()
                AND TABLE_NAME = 'fn_auditing_operation_log'
                AND COLUMN_NAME = 'DetailsExpiresAtUtc'
              """);
        Assert.AreEqual("操作详情绝对到期时间(UTC)", expiryComment);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(
            connection,
            "fn_auditing_operation_log",
            "IX_fn_auditing_operation_log_DetailsExpiresAtUtc_Id",
            sqlServer));

        var stateRows = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_auditing_details_cleanup_state");
        Assert.AreEqual(0, stateRows);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(
            connection,
            "fn_auditing_details_cleanup_state",
            "UQ_fn_auditing_details_cleanup_state_StateKey",
            sqlServer));
        var stateComment = await connection.ExecuteScalarAsync<string>(sqlServer
            ? """
              SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties
              WHERE major_id = OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state')
                AND minor_id = 0 AND name = N'MS_Description'
              """
            : """
              SELECT TABLE_COMMENT FROM information_schema.TABLES
              WHERE TABLE_SCHEMA = DATABASE()
                AND TABLE_NAME = 'fn_auditing_details_cleanup_state'
              """);
        Assert.AreEqual("操作详情清理共享检查点", stateComment);
    }

    private static void Replay239(bool sqlServer, string connectionString)
    {
        var builder = sqlServer
            ? DeployChanges.To.SqlDatabase(connectionString)
            : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(
                connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                    && name.EndsWith("239_AuditingOperationLogContext.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        Assert.HasCount(1, result.Scripts);
    }

    private static async Task MigrateThrough238Async(DatabaseProvider provider, string connectionString)
    {
        var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
        var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(segment, StringComparison.Ordinal)
                && NamingExpandTestMigrationRunner.IsThroughMigration(name, 238))
            .ToArray();
        Assert.IsTrue(names.Any(name => name.EndsWith("238_IdentityMfaRecoveryCode.sql", StringComparison.Ordinal)));
        Assert.IsFalse(names.Any(name => name.Contains("239_", StringComparison.Ordinal)));
        var workspace = Directory.CreateTempSubdirectory("fullnet-through238-").FullName;
        try
        {
            var scripts = names.Select(name => new
            {
                name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..],
            });
            File.WriteAllText(Path.Combine(workspace, "framework-manifest.json"), JsonSerializer.Serialize(new
            {
                migrationInventory = new { selectionStatus = "preset-auditing-context-recovery-through238", scripts },
            }));
            var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions
                {
                    Provider = provider,
                    ConnectionString = connectionString,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                    CommandTimeoutSeconds = 300,
                }), NullLoggerFactory.Instance,
                MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = workspace }));
            var result = await runner.MigrateAsync();
            Assert.IsTrue(result.Successful);
            Assert.AreEqual(names.Length, result.ExecutedScriptCount);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
