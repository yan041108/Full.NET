using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 102 的双库幂等恢复、发布版本不可变和活动实例唯一性。</summary>
[TestClass]
public sealed class Migration102WorkflowFirstVerticalSliceRecoveryTests
{
    private const int WorkflowTableCount = 13;

    [TestMethod]
    public async Task SqlServer_recovers_partial_schema_and_enforces_workflow_invariants()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);

        Assert.AreEqual(WorkflowTableCount, await CountSqlServerWorkflowTablesAsync(connection));
        var seed = await SeedSqlServerWorkflowAsync(connection);
        Assert.AreEqual(
            seed.FormVersionId,
            await connection.ExecuteScalarAsync<Guid>(
                "SELECT FormVersionId FROM dbo.fn_workflow_definition_version WHERE Id = @DefinitionVersionId",
                seed));
        Assert.AreEqual(
            1,
            await connection.ExecuteAsync(
                "UPDATE dbo.fn_workflow_todo SET Revision = Revision + 1 WHERE Id = @TodoId AND Revision = 1",
                seed));
        Assert.AreEqual(
            0,
            await connection.ExecuteAsync(
                "UPDATE dbo.fn_workflow_todo SET Revision = Revision + 1 WHERE Id = @TodoId AND Revision = 1",
                seed),
            "过期待办修订号不得覆盖已提交状态。");
        await Assert.ThrowsAsync<SqlException>(() => InsertSqlServerActiveInstanceAsync(connection, seed));
        await connection.ExecuteAsync(
            "UPDATE dbo.fn_workflow_instance SET StatusKey = 'completed', CompletedAtUtc = SYSUTCDATETIME(), Revision = Revision + 1 WHERE Id = @Id",
            new { Id = seed.InstanceId });
        await InsertSqlServerActiveInstanceAsync(connection, seed);
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.fn_workflow_definition_version SET ContentHash = @ContentHash WHERE Id = @Id",
            new { ContentHash = new string('b', 64), Id = seed.DefinitionVersionId }));

        await connection.ExecuteAsync(
            """
            DROP TABLE dbo.fn_workflow_domain_audit;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%102_WorkflowFirstVerticalSlice.sql';
            """);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(WorkflowTableCount, await CountSqlServerWorkflowTablesAsync(connection));
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.fn_workflow_definition WHERE Id = @Id",
                new { Id = seed.DefinitionId }),
            "恢复缺失尾表时不得破坏既有流程定义。");
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_recovers_partial_schema_and_enforces_workflow_invariants()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));

        Assert.AreEqual(WorkflowTableCount, await CountMySqlWorkflowTablesAsync(connection));
        var seed = await SeedMySqlWorkflowAsync(connection);
        Assert.AreEqual(
            seed.FormVersionId,
            await connection.ExecuteScalarAsync<Guid>(
                "SELECT FormVersionId FROM fn_workflow_definition_version WHERE Id = @DefinitionVersionId",
                seed));
        Assert.AreEqual(
            1,
            await connection.ExecuteAsync(
                "UPDATE fn_workflow_todo SET Revision = Revision + 1 WHERE Id = @TodoId AND Revision = 1",
                seed));
        Assert.AreEqual(
            0,
            await connection.ExecuteAsync(
                "UPDATE fn_workflow_todo SET Revision = Revision + 1 WHERE Id = @TodoId AND Revision = 1",
                seed),
            "过期待办修订号不得覆盖已提交状态。");
        await Assert.ThrowsAsync<MySqlException>(() => InsertMySqlActiveInstanceAsync(connection, seed));
        await connection.ExecuteAsync(
            "UPDATE fn_workflow_instance SET StatusKey = 'completed', CompletedAtUtc = UTC_TIMESTAMP(6), Revision = Revision + 1 WHERE Id = @Id",
            new { Id = seed.InstanceId });
        await InsertMySqlActiveInstanceAsync(connection, seed);
        await Assert.ThrowsAsync<MySqlException>(() => connection.ExecuteAsync(
            "UPDATE fn_workflow_definition_version SET ContentHash = @ContentHash WHERE Id = @Id",
            new { ContentHash = new string('b', 64), Id = seed.DefinitionVersionId }));

        await connection.ExecuteAsync(
            """
            DROP TABLE fn_workflow_domain_audit;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%102_WorkflowFirstVerticalSlice.sql';
            """);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(WorkflowTableCount, await CountMySqlWorkflowTablesAsync(connection));
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_workflow_definition WHERE Id = @Id",
                new { Id = seed.DefinitionId }),
            "恢复缺失尾表时不得破坏既有流程定义。");
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task<WorkflowSeed> SeedSqlServerWorkflowAsync(SqlConnection connection)
    {
        var seed = WorkflowSeed.Create();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_workflow_definition
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionKey, DraftId,
                 LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@DefinitionId, NULL, 'host', 'host', @DefinitionKey, NULL,
                 NULL, @ActorUserId, @Now, NULL, 1);

            INSERT INTO dbo.fn_workflow_form_definition
                (Id, TenantId, ScopeKey, TenantScopeKey, FormKey, DraftSchemaJson,
                 DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@FormDefinitionId, NULL, 'host', 'host', @DefinitionKey, N'{}',
                 1, @FormVersionId, @ActorUserId, @Now, NULL);

            INSERT INTO dbo.fn_workflow_form_version
                (Id, FormDefinitionId, VersionNumber, SchemaVersion, AdapterVersion,
                 ComponentCatalogVersion, FormSchemaJson, WebRenderSchemaJson,
                 ContentHash, PublishedById, PublishedAtUtc)
            VALUES
                (@FormVersionId, @FormDefinitionId, 1, 1, 1, 1, N'{}', N'{}',
                 @ContentHash, @ActorUserId, @Now);

            INSERT INTO dbo.fn_workflow_definition_version
                (Id, DefinitionId, FormVersionId, VersionNumber, SchemaVersion, CanonicalJson,
                 ContentHash, PublishedById, PublishedAtUtc)
            VALUES
                (@DefinitionVersionId, @DefinitionId, @FormVersionId, 1, 1, N'{}',
                 @ContentHash, @ActorUserId, @Now);

            INSERT INTO dbo.fn_workflow_instance
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionVersionId,
                 FormVersionId, BusinessType, BusinessId, StatusKey, Revision,
                 StartedById, StartedAtUtc)
            VALUES
                (@InstanceId, NULL, 'host', 'host', @DefinitionVersionId,
                 NULL, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);

            INSERT INTO dbo.fn_workflow_step
                (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId,
                 DueAtUtc, AttemptCount, Revision, StartedAtUtc, CompletedAtUtc)
            VALUES
                (@StepId, @InstanceId, 'approve', 'approval', 'active', @ActorUserId,
                 NULL, 0, 1, @Now, NULL);

            INSERT INTO dbo.fn_workflow_todo
                (Id, InstanceId, StepId, AssigneeUserId, StatusKey, Revision,
                 ArrivedAtUtc, CompletedAtUtc, ResultActionKey)
            VALUES
                (@TodoId, @InstanceId, @StepId, @ActorUserId, 'pending', 1,
                 @Now, NULL, NULL);
            """,
            seed);
        return seed;
    }

    private static async Task<WorkflowSeed> SeedMySqlWorkflowAsync(MySqlConnection connection)
    {
        var seed = WorkflowSeed.Create();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_workflow_definition
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionKey, DraftId,
                 LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@DefinitionId, NULL, 'host', 'host', @DefinitionKey, NULL,
                 NULL, @ActorUserId, @Now, NULL, 1);

            INSERT INTO fn_workflow_form_definition
                (Id, TenantId, ScopeKey, TenantScopeKey, FormKey, DraftSchemaJson,
                 DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@FormDefinitionId, NULL, 'host', 'host', @DefinitionKey, '{}',
                 1, @FormVersionId, @ActorUserId, @Now, NULL);

            INSERT INTO fn_workflow_form_version
                (Id, FormDefinitionId, VersionNumber, SchemaVersion, AdapterVersion,
                 ComponentCatalogVersion, FormSchemaJson, WebRenderSchemaJson,
                 ContentHash, PublishedById, PublishedAtUtc)
            VALUES
                (@FormVersionId, @FormDefinitionId, 1, 1, 1, 1, '{}', '{}',
                 @ContentHash, @ActorUserId, @Now);

            INSERT INTO fn_workflow_definition_version
                (Id, DefinitionId, FormVersionId, VersionNumber, SchemaVersion, CanonicalJson,
                 ContentHash, PublishedById, PublishedAtUtc)
            VALUES
                (@DefinitionVersionId, @DefinitionId, @FormVersionId, 1, 1, '{}',
                 @ContentHash, @ActorUserId, @Now);

            INSERT INTO fn_workflow_instance
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionVersionId,
                 FormVersionId, BusinessType, BusinessId, StatusKey, Revision,
                 StartedById, StartedAtUtc)
            VALUES
                (@InstanceId, NULL, 'host', 'host', @DefinitionVersionId,
                 NULL, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);

            INSERT INTO fn_workflow_step
                (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId,
                 DueAtUtc, AttemptCount, Revision, StartedAtUtc, CompletedAtUtc)
            VALUES
                (@StepId, @InstanceId, 'approve', 'approval', 'active', @ActorUserId,
                 NULL, 0, 1, @Now, NULL);

            INSERT INTO fn_workflow_todo
                (Id, InstanceId, StepId, AssigneeUserId, StatusKey, Revision,
                 ArrivedAtUtc, CompletedAtUtc, ResultActionKey)
            VALUES
                (@TodoId, @InstanceId, @StepId, @ActorUserId, 'pending', 1,
                 @Now, NULL, NULL);
            """,
            seed);
        return seed;
    }

    private static Task<int> InsertSqlServerActiveInstanceAsync(
        SqlConnection connection,
        WorkflowSeed seed) =>
        connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_workflow_instance
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionVersionId,
                 FormVersionId, BusinessType, BusinessId, StatusKey, Revision,
                 StartedById, StartedAtUtc)
            VALUES
                (@NextInstanceId, NULL, 'host', 'host', @DefinitionVersionId,
                 NULL, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);
            """,
            seed);

    private static Task<int> InsertMySqlActiveInstanceAsync(
        MySqlConnection connection,
        WorkflowSeed seed) =>
        connection.ExecuteAsync(
            """
            INSERT INTO fn_workflow_instance
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionVersionId,
                 FormVersionId, BusinessType, BusinessId, StatusKey, Revision,
                 StartedById, StartedAtUtc)
            VALUES
                (@NextInstanceId, NULL, 'host', 'host', @DefinitionVersionId,
                 NULL, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);
            """,
            seed);

    private static Task<int> CountSqlServerWorkflowTablesAsync(SqlConnection connection) =>
        connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name LIKE N'fn_workflow[_]%'");

    private static Task<int> CountMySqlWorkflowTablesAsync(MySqlConnection connection) =>
        connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME LIKE 'fn\\_workflow\\_%'");

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

    private sealed record WorkflowSeed(
        Guid DefinitionId,
        Guid DefinitionVersionId,
        Guid FormDefinitionId,
        Guid FormVersionId,
        Guid InstanceId,
        Guid NextInstanceId,
        Guid StepId,
        Guid TodoId,
        Guid ActorUserId,
        string DefinitionKey,
        string BusinessType,
        string BusinessId,
        string ContentHash,
        DateTime Now)
    {
        public static WorkflowSeed Create()
        {
            var nonce = Guid.CreateVersion7().ToString("N");
            return new WorkflowSeed(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                $"definition-{nonce}",
                "integration-test",
                nonce,
                new string('a', 64),
                DateTime.UtcNow);
        }
    }
}
