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
/// 验证已记账的 094 不会被篡改，并由后续 095 无损收敛约束与基线所有权。
/// </summary>
[TestClass]
public sealed class Migration095MessagingStreamOwnershipRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_stream_ownership_migration_recovers_check_constraints_without_dropping_data()
    {
        var connectionString = await SharedDatabaseFixture
            .CreateSqlServerDatabaseAsync()
            .ConfigureAwait(false);
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);

        await using var connection = new SqlConnection(connectionString);
        var now = DateTimeOffset.UtcNow;
        var eventId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_messaging_stream_ownership
                    (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                     CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
                     Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@MessageType, @SchemaVersion, @TopicCode, @CurrentOwner, @PreviousOwner,
                     @CutoffEventId, @CutoffOccurredAtUtc, NULL, NULL,
                     @Reason, NULL, NULL, @CreatedAtUtc, @UpdatedAtUtc);
                IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_fn_messaging_stream_ownership_SchemaVersion')
                BEGIN
                    ALTER TABLE dbo.fn_messaging_stream_ownership
                        DROP CONSTRAINT CK_fn_messaging_stream_ownership_SchemaVersion;
                END;
                DELETE FROM dbo.SchemaVersions
                WHERE ScriptName LIKE '%095_MessagingStreamOwnershipConvergence.sql';
                """,
                new
                {
                    MessageType = "fullnet.messaging.stream.ownership.recovery.event",
                    SchemaVersion = 1,
                    TopicCode = "fn-messaging-stream-ownership-recovery",
                    CurrentOwner = 0,
                    PreviousOwner = 0,
                    CutoffEventId = eventId,
                    CutoffOccurredAtUtc = now,
                    Reason = "recovery test",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                })
            .ConfigureAwait(false);

        var recovered = await runner.MigrateAsync().ConfigureAwait(false);

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.fn_messaging_stream_ownership
            """).ConfigureAwait(false));
        var hasSchemaVersionCheck = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.check_constraints
            WHERE name = 'CK_fn_messaging_stream_ownership_SchemaVersion'
            """).ConfigureAwait(false);
        Assert.AreEqual(1, hasSchemaVersionCheck);
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_stream_ownership_migration_recovers_check_constraints_without_dropping_data()
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
        var now = DateTime.UtcNow;
        var eventId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
                """
                INSERT INTO fn_messaging_stream_ownership
                    (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                     CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
                     Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@MessageType, @SchemaVersion, @TopicCode, @CurrentOwner, @PreviousOwner,
                     @CutoffEventId, @CutoffOccurredAtUtc, NULL, NULL,
                     @Reason, NULL, NULL, @CreatedAtUtc, @UpdatedAtUtc);
                ALTER TABLE fn_messaging_stream_ownership
                    DROP CHECK CK_fn_messaging_stream_ownership_SchemaVersion;
                DELETE FROM schemaversions
                WHERE ScriptName LIKE '%095_MessagingStreamOwnershipConvergence.sql';
                """,
                new
                {
                    MessageType = "fullnet.messaging.stream.ownership.recovery.event",
                    SchemaVersion = 1,
                    TopicCode = "fn-messaging-stream-ownership-recovery",
                    CurrentOwner = 0,
                    PreviousOwner = 0,
                    CutoffEventId = eventId,
                    CutoffOccurredAtUtc = now,
                    Reason = "recovery test",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                })
            .ConfigureAwait(false);

        var recovered = await runner.MigrateAsync().ConfigureAwait(false);

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM fn_messaging_stream_ownership
            """).ConfigureAwait(false));
        var hasSchemaVersionCheck = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM information_schema.TABLE_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_messaging_stream_ownership'
              AND CONSTRAINT_NAME = 'CK_fn_messaging_stream_ownership_SchemaVersion'
              AND CONSTRAINT_TYPE = 'CHECK'
            """).ConfigureAwait(false);
        Assert.AreEqual(1, hasSchemaVersionCheck);
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
