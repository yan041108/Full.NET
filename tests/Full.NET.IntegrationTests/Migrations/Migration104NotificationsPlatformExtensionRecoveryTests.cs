using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 104 的双库幂等恢复、Intent 业务幂等、回执去重和发布版本不可变。</summary>
[TestClass]
public sealed class Migration104NotificationsPlatformExtensionRecoveryTests
{
    // 104 之后新增公告用户/机构受众表与收件端点验证挑战表，平台表总数必须随正式迁移演进。
    private const int PlatformTableCount = 17;

    [TestMethod]
    public async Task SqlServer_recovers_partial_schema_and_enforces_platform_invariants()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);

        Assert.AreEqual(PlatformTableCount, await CountSqlServerPlatformTablesAsync(connection));
        var seed = await SeedSqlServerAsync(connection);
        Assert.AreEqual(
            1,
            await connection.ExecuteAsync(
                "UPDATE dbo.fn_notifications_delivery SET Revision = Revision + 1 WHERE Id = @DeliveryId AND Revision = 1",
                seed));
        Assert.AreEqual(
            0,
            await connection.ExecuteAsync(
                "UPDATE dbo.fn_notifications_delivery SET Revision = Revision + 1 WHERE Id = @DeliveryId AND Revision = 1",
                seed),
            "过期投递修订号不得覆盖已提交状态。");
        await Assert.ThrowsAsync<SqlException>(() => InsertSqlServerIntentAsync(connection, seed, seed.DuplicateIntentId));
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "INSERT INTO dbo.fn_notifications_receipt (Id, ProviderTypeKey, ReceiptIdempotencyKey, ExternalStatusKey, MappedStatusKey, PayloadDigest, ReceivedAtUtc, ProcessStatusKey) VALUES (@DuplicateReceiptId, 'test', @ReceiptIdempotencyKey, 'sent', 'sent', @ContentHash, SYSUTCDATETIME(), 'processed')",
            seed));
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.fn_notifications_template_version SET ContentHash = @NextHash WHERE Id = @TemplateVersionId",
            seed));

        await connection.ExecuteAsync(
            """
            DROP TABLE dbo.fn_notifications_domain_audit;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%104_NotificationsPlatformExtension.sql';
            """);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(PlatformTableCount, await CountSqlServerPlatformTablesAsync(connection));
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.fn_notifications_intent WHERE Id = @IntentId",
                seed),
            "恢复缺失尾表时不得破坏既有 Intent。");
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_recovers_partial_schema_and_enforces_platform_invariants()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));

        Assert.AreEqual(PlatformTableCount, await CountMySqlPlatformTablesAsync(connection));
        var seed = await SeedMySqlAsync(connection);
        Assert.AreEqual(
            1,
            await connection.ExecuteAsync(
                "UPDATE fn_notifications_delivery SET Revision = Revision + 1 WHERE Id = @DeliveryId AND Revision = 1",
                seed));
        Assert.AreEqual(
            0,
            await connection.ExecuteAsync(
                "UPDATE fn_notifications_delivery SET Revision = Revision + 1 WHERE Id = @DeliveryId AND Revision = 1",
                seed),
            "过期投递修订号不得覆盖已提交状态。");
        await Assert.ThrowsAsync<MySqlException>(() => InsertMySqlIntentAsync(connection, seed, seed.DuplicateIntentId));
        await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            "INSERT INTO fn_notifications_receipt (Id, ProviderTypeKey, ReceiptIdempotencyKey, ExternalStatusKey, MappedStatusKey, PayloadDigest, ReceivedAtUtc, ProcessStatusKey) VALUES (@DuplicateReceiptId, 'test', @ReceiptIdempotencyKey, 'sent', 'sent', @ContentHash, UTC_TIMESTAMP(6), 'processed')",
            seed));
        await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            "UPDATE fn_notifications_template_version SET ContentHash = @NextHash WHERE Id = @TemplateVersionId",
            seed));

        await connection.ExecuteAsync(
            """
            DROP TABLE fn_notifications_domain_audit;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%104_NotificationsPlatformExtension.sql';
            """);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(PlatformTableCount, await CountMySqlPlatformTablesAsync(connection));
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_notifications_intent WHERE Id = @IntentId",
                seed),
            "恢复缺失尾表时不得破坏既有 Intent。");
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task<PlatformSeed> SeedSqlServerAsync(SqlConnection connection)
    {
        var seed = PlatformSeed.Create();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_template
                (Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, ChannelKey, ContentCategoryKey,
                 DraftSubject, DraftBodyJson, DraftParameterSchemaJson, DraftRevision,
                 LatestPublishedVersionId, CreatedById, CreatedAtUtc, Version)
            VALUES
                (@TemplateId, NULL, 'host', 'host', @TemplateKey, 'inbox', 'informational',
                 N'title', N'{}', N'{}', 1, @TemplateVersionId, @ActorUserId, @Now, 1);

            INSERT INTO dbo.fn_notifications_template_version
                (Id, TemplateId, VersionNumber, SchemaVersion, Subject, BodyJson, ParameterSchemaJson,
                 ContentClassificationKey, ContentHash, PublishedById, PublishedAtUtc)
            VALUES
                (@TemplateVersionId, @TemplateId, 1, 1, N'title', N'{}', N'{}',
                 'c0', @ContentHash, @ActorUserId, @Now);

            INSERT INTO dbo.fn_notifications_intent
                (Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
                 TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
                 RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision)
            VALUES
                (@IntentId, NULL, 'host', 'host', @ProducerKey, @SceneKey, @IdempotencyKey,
                 @TemplateVersionId, NULL, 'informational', 'single',
                 N'[]', N'{}', 'accepted', @ActorUserId, @Now, 1);

            INSERT INTO dbo.fn_notifications_recipient
                (Id, IntentId, RecipientTypeKey, RecipientKey, UserId, AddressDigest,
                 ResolutionStatusKey, CreatedAtUtc)
            VALUES
                (@RecipientId, @IntentId, 'user', @RecipientKey, @ActorUserId, NULL,
                 'resolved', @Now);

            INSERT INTO dbo.fn_notifications_delivery
                (Id, IntentId, RecipientId, ChannelKey, ProviderProfileVersionId, BindingVersionId,
                 StatusKey, Revision, LeaseGeneration, CreatedAtUtc)
            VALUES
                (@DeliveryId, @IntentId, @RecipientId, 'inbox', NULL, NULL,
                 'persisted', 1, 1, @Now);

            INSERT INTO dbo.fn_notifications_receipt
                (Id, ProviderTypeKey, ReceiptIdempotencyKey, DeliveryId, ExternalStatusKey,
                 MappedStatusKey, PayloadDigest, ReceivedAtUtc, ProcessStatusKey)
            VALUES
                (@ReceiptId, 'test', @ReceiptIdempotencyKey, @DeliveryId, 'sent',
                 'sent', @ContentHash, @Now, 'processed');
            """,
            seed);
        return seed;
    }

    private static async Task<PlatformSeed> SeedMySqlAsync(MySqlConnection connection)
    {
        var seed = PlatformSeed.Create();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_template
                (Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, ChannelKey, ContentCategoryKey,
                 DraftSubject, DraftBodyJson, DraftParameterSchemaJson, DraftRevision,
                 LatestPublishedVersionId, CreatedById, CreatedAtUtc, Version)
            VALUES
                (@TemplateId, NULL, 'host', 'host', @TemplateKey, 'inbox', 'informational',
                 'title', '{}', '{}', 1, @TemplateVersionId, @ActorUserId, @Now, 1);

            INSERT INTO fn_notifications_template_version
                (Id, TemplateId, VersionNumber, SchemaVersion, Subject, BodyJson, ParameterSchemaJson,
                 ContentClassificationKey, ContentHash, PublishedById, PublishedAtUtc)
            VALUES
                (@TemplateVersionId, @TemplateId, 1, 1, 'title', '{}', '{}',
                 'c0', @ContentHash, @ActorUserId, @Now);

            INSERT INTO fn_notifications_intent
                (Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
                 TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
                 RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision)
            VALUES
                (@IntentId, NULL, 'host', 'host', @ProducerKey, @SceneKey, @IdempotencyKey,
                 @TemplateVersionId, NULL, 'informational', 'single',
                 '[]', '{}', 'accepted', @ActorUserId, @Now, 1);

            INSERT INTO fn_notifications_recipient
                (Id, IntentId, RecipientTypeKey, RecipientKey, UserId, AddressDigest,
                 ResolutionStatusKey, CreatedAtUtc)
            VALUES
                (@RecipientId, @IntentId, 'user', @RecipientKey, @ActorUserId, NULL,
                 'resolved', @Now);

            INSERT INTO fn_notifications_delivery
                (Id, IntentId, RecipientId, ChannelKey, ProviderProfileVersionId, BindingVersionId,
                 StatusKey, Revision, LeaseGeneration, CreatedAtUtc)
            VALUES
                (@DeliveryId, @IntentId, @RecipientId, 'inbox', NULL, NULL,
                 'persisted', 1, 1, @Now);

            INSERT INTO fn_notifications_receipt
                (Id, ProviderTypeKey, ReceiptIdempotencyKey, DeliveryId, ExternalStatusKey,
                 MappedStatusKey, PayloadDigest, ReceivedAtUtc, ProcessStatusKey)
            VALUES
                (@ReceiptId, 'test', @ReceiptIdempotencyKey, @DeliveryId, 'sent',
                 'sent', @ContentHash, @Now, 'processed');
            """,
            seed);
        return seed;
    }

    private static Task<int> InsertSqlServerIntentAsync(
        SqlConnection connection,
        PlatformSeed seed,
        Guid intentId) =>
        connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_intent
                (Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
                 TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
                 RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision)
            VALUES
                (@Id, NULL, 'host', 'host', @ProducerKey, @SceneKey, @IdempotencyKey,
                 @TemplateVersionId, NULL, 'informational', 'single',
                 N'[]', N'{}', 'accepted', @ActorUserId, @Now, 1);
            """,
            new
            {
                Id = intentId,
                seed.ProducerKey,
                seed.SceneKey,
                seed.IdempotencyKey,
                seed.TemplateVersionId,
                seed.ActorUserId,
                seed.Now,
            });

    private static Task<int> InsertMySqlIntentAsync(
        MySqlConnection connection,
        PlatformSeed seed,
        Guid intentId) =>
        connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_intent
                (Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
                 TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
                 RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision)
            VALUES
                (@Id, NULL, 'host', 'host', @ProducerKey, @SceneKey, @IdempotencyKey,
                 @TemplateVersionId, NULL, 'informational', 'single',
                 '[]', '{}', 'accepted', @ActorUserId, @Now, 1);
            """,
            new
            {
                Id = intentId,
                seed.ProducerKey,
                seed.SceneKey,
                seed.IdempotencyKey,
                seed.TemplateVersionId,
                seed.ActorUserId,
                seed.Now,
            });

    private static Task<int> CountSqlServerPlatformTablesAsync(SqlConnection connection) =>
        connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.tables
            WHERE schema_id = SCHEMA_ID(N'dbo')
              AND name LIKE N'fn_notifications[_]%'
              AND name NOT IN (N'fn_notifications_announcement', N'fn_notifications_inbox_message')
            """);

    private static Task<int> CountMySqlPlatformTablesAsync(MySqlConnection connection) =>
        connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME LIKE 'fn\\_notifications\\_%'
              AND TABLE_NAME NOT IN ('fn_notifications_announcement', 'fn_notifications_inbox_message')
            """);

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

    private sealed record PlatformSeed(
        Guid TemplateId,
        Guid TemplateVersionId,
        Guid IntentId,
        Guid DuplicateIntentId,
        Guid RecipientId,
        Guid DeliveryId,
        Guid ReceiptId,
        Guid DuplicateReceiptId,
        Guid ActorUserId,
        string TemplateKey,
        string ProducerKey,
        string SceneKey,
        string IdempotencyKey,
        string RecipientKey,
        string ReceiptIdempotencyKey,
        string ContentHash,
        string NextHash,
        DateTime Now)
    {
        public static PlatformSeed Create()
        {
            var nonce = Guid.CreateVersion7().ToString("N");
            return new PlatformSeed(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                $"template-{nonce}",
                "workflow",
                "todo-assigned",
                $"idem-{nonce}",
                nonce,
                $"receipt-{nonce}",
                new string('a', 64),
                new string('b', 64),
                DateTime.UtcNow);
        }
    }
}
