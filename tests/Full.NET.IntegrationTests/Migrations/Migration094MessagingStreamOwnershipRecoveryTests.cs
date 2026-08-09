using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// 验证已发布 094 在完整升级后可按原始脚本重新记账，且不会破坏既有数据。
/// MySQL 由迁移执行器精确跳过旧脚本中不受支持的约束语法，约束收敛属于 095。
/// </summary>
[TestClass]
public sealed class Migration094MessagingStreamOwnershipRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_published_094_reexecutes_without_mutating_existing_data()
    {
        var connectionString = await SharedDatabaseFixture
            .CreateSqlServerDatabaseAsync()
            .ConfigureAwait(false);
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);

        await using var connection = new SqlConnection(connectionString);
        var messageType = "fullnet.messaging.stream.ownership.094-recovery";
        await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_messaging_stream_ownership
                    (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                     CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
                     Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
                     RollbackState, RollbackGeneration, RollbackPreparedAtUtc,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@MessageType, 1, 'ownership-094-recovery', 0, 0,
                     @EventId, SYSUTCDATETIME(), NULL, NULL,
                     N'published migration recovery', NULL, NULL,
                     0, NULL, NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
                DELETE FROM dbo.SchemaVersions
                WHERE ScriptName LIKE '%094_MessagingStreamOwnership.sql';
                """,
                new { MessageType = messageType, EventId = Guid.CreateVersion7() })
            .ConfigureAwait(false);

        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.fn_messaging_stream_ownership
            WHERE MessageType = @MessageType
            """,
            new { MessageType = messageType }).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_published_094_reexecutes_without_mutating_existing_data()
    {
        var connectionString = await SharedDatabaseFixture
            .CreateMySqlDatabaseAsync()
            .ConfigureAwait(false);
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var messageType = "fullnet.messaging.stream.ownership.094-recovery";
        await connection.ExecuteAsync(
                """
                INSERT INTO fn_messaging_stream_ownership
                    (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                     CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
                     Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
                     RollbackState, RollbackGeneration, RollbackPreparedAtUtc,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@MessageType, 1, 'ownership-094-recovery', 0, 0,
                     @EventId, UTC_TIMESTAMP(6), NULL, NULL,
                     'published migration recovery', NULL, NULL,
                     0, NULL, NULL, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
                DELETE FROM schemaversions
                WHERE ScriptName LIKE '%094_MessagingStreamOwnership.sql';
                """,
                new { MessageType = messageType, EventId = Guid.CreateVersion7() })
            .ConfigureAwait(false);

        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_messaging_stream_ownership
            WHERE MessageType = @MessageType
            """,
            new { MessageType = messageType }).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
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
}
