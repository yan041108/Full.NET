using System.Data;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 105 的 Inbox Scope/Intent 幂等、RecipientEndpoint 隔离与部分完成后恢复。</summary>
[TestClass]
public sealed class Migration105NotificationsInboxScopeExtensionRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_recovers_inbox_scope_columns_and_enforces_isolation()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);
        var seed = await SeedPlatformAsync(connection, sqlServer: true);

        Assert.AreEqual("NO", await connection.ExecuteScalarAsync<string>(
            "SELECT CASE WHEN is_nullable = 0 THEN 'NO' ELSE 'YES' END FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message') AND name = N'ScopeKey'"));
        await AssertInboxAndEndpointInvariantsSqlServerAsync(connection, seed);

        var preservedId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey)
            VALUES
                (@PreservedId, NULL, @ActorUserId, N'保留', N'正文', 'unread', SYSUTCDATETIME(), 'host', N'host');
            """,
            new { PreservedId = preservedId, seed.ActorUserId });

        await connection.ExecuteAsync(
            """
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_notifications_inbox_Intent_Recipient' AND object_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message'))
                DROP INDEX UX_fn_notifications_inbox_Intent_Recipient ON dbo.fn_notifications_inbox_message;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_notifications_inbox_Scope_Unread' AND object_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message'))
                DROP INDEX IX_fn_notifications_inbox_Scope_Unread ON dbo.fn_notifications_inbox_message;
            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_fn_notifications_inbox_message_Intent')
                ALTER TABLE dbo.fn_notifications_inbox_message DROP CONSTRAINT FK_fn_notifications_inbox_message_Intent;
            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_fn_notifications_inbox_message_ScopeKey')
                ALTER TABLE dbo.fn_notifications_inbox_message DROP CONSTRAINT CK_fn_notifications_inbox_message_ScopeKey;
            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_fn_notifications_endpoint_Verification')
                ALTER TABLE dbo.fn_notifications_recipient_endpoint DROP CONSTRAINT CK_fn_notifications_endpoint_Verification;
            IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_fn_notifications_inbox_message_ScopeKey')
                ALTER TABLE dbo.fn_notifications_inbox_message DROP CONSTRAINT DF_fn_notifications_inbox_message_ScopeKey;
            IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_fn_notifications_inbox_message_TenantScopeKey')
                ALTER TABLE dbo.fn_notifications_inbox_message DROP CONSTRAINT DF_fn_notifications_inbox_message_TenantScopeKey;
            ALTER TABLE dbo.fn_notifications_inbox_message DROP COLUMN ScopeKey, TenantScopeKey, IntentId;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%105_NotificationsInboxScopeExtension.sql';
            """);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.fn_notifications_inbox_message WHERE Id = @PreservedId AND ScopeKey = 'host' AND TenantScopeKey = N'host'",
                new { PreservedId = preservedId }),
            "恢复缺失 Scope 列时必须回填 Host 存量行。");
        await AssertUniqueConstraintsRemainSqlServerAsync(connection, seed);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_recovers_inbox_scope_columns_and_enforces_isolation()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var seed = await SeedPlatformAsync(connection, sqlServer: false);

        Assert.AreEqual("NO", await connection.ExecuteScalarAsync<string>(
            "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_inbox_message' AND COLUMN_NAME = 'ScopeKey'"));
        await AssertInboxAndEndpointInvariantsMySqlAsync(connection, seed);

        var preservedId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey)
            VALUES
                (@PreservedId, NULL, @ActorUserId, '保留', '正文', 'unread', UTC_TIMESTAMP(6), 'host', 'host');
            """,
            new { PreservedId = preservedId, seed.ActorUserId });

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_notifications_inbox_message DROP INDEX UX_fn_notifications_inbox_Intent_Recipient;
            ALTER TABLE fn_notifications_inbox_message DROP INDEX IX_fn_notifications_inbox_Scope_Unread;
            ALTER TABLE fn_notifications_inbox_message DROP FOREIGN KEY FK_fn_notifications_inbox_message_Intent;
            ALTER TABLE fn_notifications_inbox_message DROP CHECK CK_fn_notifications_inbox_message_ScopeKey;
            ALTER TABLE fn_notifications_recipient_endpoint DROP CHECK CK_fn_notifications_endpoint_Verification;
            ALTER TABLE fn_notifications_inbox_message DROP COLUMN ScopeKey, DROP COLUMN TenantScopeKey, DROP COLUMN IntentId;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%105_NotificationsInboxScopeExtension.sql';
            """);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_notifications_inbox_message WHERE Id = @PreservedId AND ScopeKey = 'host' AND TenantScopeKey = 'host'",
                new { PreservedId = preservedId }),
            "恢复缺失 Scope 列时必须回填 Host 存量行。");
        await AssertUniqueConstraintsRemainMySqlAsync(connection, seed);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task AssertInboxAndEndpointInvariantsSqlServerAsync(
        SqlConnection connection,
        ScopeSeed seed)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@InboxId, NULL, @ActorUserId, N'意图', N'正文', 'unread', SYSUTCDATETIME(), 'host', N'host', @IntentId);
            """,
            seed);
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@DuplicateInboxId, NULL, @ActorUserId, N'重复', N'正文', 'unread', SYSUTCDATETIME(), 'host', N'host', @IntentId);
            """,
            seed));
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_recipient_endpoint
                (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey,
                 ProtectedValue, MaskedValue, VerificationStatusKey, CreatedAtUtc)
            VALUES
                (@HostEndpointId, NULL, 'host', N'host', @ActorUserId, @ProfileVersionId, 'email',
                 N'protected-host', N'a***@***.com', 'pending', SYSUTCDATETIME()),
                (@TenantEndpointId, @TenantId, 'tenant', @TenantScopeKey, @ActorUserId, @ProfileVersionId, 'email',
                 N'protected-tenant', N'b***@***.com', 'verified', SYSUTCDATETIME());
            """,
            seed);
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_recipient_endpoint
                (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey,
                 ProtectedValue, MaskedValue, VerificationStatusKey, CreatedAtUtc)
            VALUES
                (@DuplicateEndpointId, NULL, 'host', N'host', @ActorUserId, @ProfileVersionId, 'email',
                 N'protected-dup', N'c***@***.com', 'pending', SYSUTCDATETIME());
            """,
            seed));
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_recipient_endpoint
                (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey,
                 ProtectedValue, MaskedValue, VerificationStatusKey, CreatedAtUtc)
            VALUES
                (@InvalidStatusEndpointId, NULL, 'host', N'host', @ActorUserId, @ProfileVersionId, 'sms',
                 N'protected', N'****1234', 'unknown', SYSUTCDATETIME());
            """,
            seed));
    }

    private static async Task AssertUniqueConstraintsRemainSqlServerAsync(
        SqlConnection connection,
        ScopeSeed seed)
    {
        var inboxId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@InboxId, NULL, @ActorUserId, N'恢复后', N'正文', 'unread', SYSUTCDATETIME(), 'host', N'host', @IntentId);
            """,
            new { InboxId = inboxId, seed.ActorUserId, seed.IntentId });
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@DuplicateInboxId, NULL, @ActorUserId, N'重复', N'正文', 'unread', SYSUTCDATETIME(), 'host', N'host', @IntentId);
            """,
            new { DuplicateInboxId = Guid.CreateVersion7(), seed.ActorUserId, seed.IntentId }));
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_notifications_recipient_endpoint
                (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey,
                 ProtectedValue, MaskedValue, VerificationStatusKey, CreatedAtUtc)
            VALUES
                (@DuplicateEndpointId, NULL, 'host', N'host', @ActorUserId, @ProfileVersionId, 'email',
                 N'protected-dup', N'c***@***.com', 'pending', SYSUTCDATETIME());
            """,
            new { DuplicateEndpointId = Guid.CreateVersion7(), seed.ActorUserId, seed.ProfileVersionId }));
    }

    private static async Task AssertInboxAndEndpointInvariantsMySqlAsync(
        MySqlConnection connection,
        ScopeSeed seed)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@InboxId, NULL, @ActorUserId, '意图', '正文', 'unread', UTC_TIMESTAMP(6), 'host', 'host', @IntentId);
            """,
            seed);
        await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@DuplicateInboxId, NULL, @ActorUserId, '重复', '正文', 'unread', UTC_TIMESTAMP(6), 'host', 'host', @IntentId);
            """,
            seed));
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_recipient_endpoint
                (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey,
                 ProtectedValue, MaskedValue, VerificationStatusKey, CreatedAtUtc)
            VALUES
                (@HostEndpointId, NULL, 'host', 'host', @ActorUserId, @ProfileVersionId, 'email',
                 'protected-host', 'a***@***.com', 'pending', UTC_TIMESTAMP(6)),
                (@TenantEndpointId, @TenantId, 'tenant', @TenantScopeKey, @ActorUserId, @ProfileVersionId, 'email',
                 'protected-tenant', 'b***@***.com', 'verified', UTC_TIMESTAMP(6));
            """,
            seed);
        await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_recipient_endpoint
                (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey,
                 ProtectedValue, MaskedValue, VerificationStatusKey, CreatedAtUtc)
            VALUES
                (@DuplicateEndpointId, NULL, 'host', 'host', @ActorUserId, @ProfileVersionId, 'email',
                 'protected-dup', 'c***@***.com', 'pending', UTC_TIMESTAMP(6));
            """,
            seed));
        await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_recipient_endpoint
                (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey,
                 ProtectedValue, MaskedValue, VerificationStatusKey, CreatedAtUtc)
            VALUES
                (@InvalidStatusEndpointId, NULL, 'host', 'host', @ActorUserId, @ProfileVersionId, 'sms',
                 'protected', '****1234', 'unknown', UTC_TIMESTAMP(6));
            """,
            seed));
    }

    private static async Task AssertUniqueConstraintsRemainMySqlAsync(
        MySqlConnection connection,
        ScopeSeed seed)
    {
        var inboxId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@InboxId, NULL, @ActorUserId, '恢复后', '正文', 'unread', UTC_TIMESTAMP(6), 'host', 'host', @IntentId);
            """,
            new { InboxId = inboxId, seed.ActorUserId, seed.IntentId });
        await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_inbox_message
                (Id, TenantId, RecipientUserId, Title, Content, Status, CreatedAtUtc, ScopeKey, TenantScopeKey, IntentId)
            VALUES
                (@DuplicateInboxId, NULL, @ActorUserId, '重复', '正文', 'unread', UTC_TIMESTAMP(6), 'host', 'host', @IntentId);
            """,
            new { DuplicateInboxId = Guid.CreateVersion7(), seed.ActorUserId, seed.IntentId }));
        await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO fn_notifications_recipient_endpoint
                (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey,
                 ProtectedValue, MaskedValue, VerificationStatusKey, CreatedAtUtc)
            VALUES
                (@DuplicateEndpointId, NULL, 'host', 'host', @ActorUserId, @ProfileVersionId, 'email',
                 'protected-dup', 'c***@***.com', 'pending', UTC_TIMESTAMP(6));
            """,
            new { DuplicateEndpointId = Guid.CreateVersion7(), seed.ActorUserId, seed.ProfileVersionId }));
    }

    private static async Task<ScopeSeed> SeedPlatformAsync(IDbConnection connection, bool sqlServer)
    {
        var seed = ScopeSeed.Create();
        var utc = sqlServer ? "SYSUTCDATETIME()" : "UTC_TIMESTAMP(6)";
        var prefix = sqlServer ? "dbo." : string.Empty;
        await connection.ExecuteAsync(
            $$"""
            INSERT INTO {{prefix}}fn_notifications_template
                (Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, ChannelKey, ContentCategoryKey,
                 DraftSubject, DraftBodyJson, DraftParameterSchemaJson, DraftRevision,
                 LatestPublishedVersionId, CreatedById, CreatedAtUtc, Version)
            VALUES
                (@TemplateId, NULL, 'host', 'host', @TemplateKey, 'inbox', 'informational',
                 'title', '{}', '{}', 1, @TemplateVersionId, @ActorUserId, {{utc}}, 1);

            INSERT INTO {{prefix}}fn_notifications_template_version
                (Id, TemplateId, VersionNumber, SchemaVersion, Subject, BodyJson, ParameterSchemaJson,
                 ContentClassificationKey, ContentHash, PublishedById, PublishedAtUtc)
            VALUES
                (@TemplateVersionId, @TemplateId, 1, 1, 'title', '{}', '{}',
                 'c0', @ContentHash, @ActorUserId, {{utc}});

            INSERT INTO {{prefix}}fn_notifications_intent
                (Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
                 TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
                 RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision)
            VALUES
                (@IntentId, NULL, 'host', 'host', @ProducerKey, @SceneKey, @IdempotencyKey,
                 @TemplateVersionId, NULL, 'informational', 'single',
                 '[]', '{}', 'accepted', @ActorUserId, {{utc}}, 1);

            INSERT INTO {{prefix}}fn_notifications_provider_profile
                (Id, TenantId, ScopeKey, TenantScopeKey, ProfileKey, ProviderTypeKey, NonSecretConfigJson,
                 SecretReference, IsEnabled, DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, Version)
            VALUES
                (@ProfileId, NULL, 'host', 'host', @ProfileKey, 'test', '{}',
                 'secret-ref', 1, 1, @ProfileVersionId, @ActorUserId, {{utc}}, 1);

            INSERT INTO {{prefix}}fn_notifications_provider_profile_version
                (Id, ProfileId, VersionNumber, ProviderTypeKey, AdapterVersion, NonSecretConfigJson,
                 SecretReference, ContentHash, PublishedById, PublishedAtUtc)
            VALUES
                (@ProfileVersionId, @ProfileId, 1, 'test', '1.0', '{}',
                 'secret-ref', @ContentHash, @ActorUserId, {{utc}});
            """,
            seed);
        return seed;
    }

    private static DbUpMigrationRunner CreateRunner(DatabaseProvider provider, string connectionString) =>
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

    private sealed record ScopeSeed(
        Guid TemplateId,
        Guid TemplateVersionId,
        Guid IntentId,
        Guid InboxId,
        Guid DuplicateInboxId,
        Guid ProfileId,
        Guid ProfileVersionId,
        Guid HostEndpointId,
        Guid TenantEndpointId,
        Guid DuplicateEndpointId,
        Guid InvalidStatusEndpointId,
        Guid ActorUserId,
        Guid TenantId,
        string TenantScopeKey,
        string TemplateKey,
        string ProducerKey,
        string SceneKey,
        string IdempotencyKey,
        string ProfileKey,
        string ContentHash)
    {
        public static ScopeSeed Create()
        {
            var nonce = Guid.CreateVersion7().ToString("N")[..12];
            var tenantId = Guid.CreateVersion7();
            return new(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                tenantId,
                $"tenant:{tenantId:N}",
                $"tpl-{nonce}",
                $"prod-{nonce}",
                $"scene-{nonce}",
                $"idem-{nonce}",
                $"profile-{nonce}",
                new string('a', 64));
        }
    }
}
