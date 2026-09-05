using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 110 恢复任务表可从被删除状态恢复，并保持未关闭占用唯一。</summary>
[TestClass]
public sealed class Migration110WorkflowRecoveryTaskRecoveryTests
{
    /// <summary>SQL Server 删除恢复任务表后必须能重跑 110 并阻止第二条未关闭占用。</summary>
    [TestMethod]
    public async Task SqlServer_recovers_dropped_recovery_task_table_and_blocks_duplicate_open_occupancy()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);
        var seed = await SeedSqlServerAsync(connection);

        await InsertSqlServerTaskAsync(connection, seed, seed.TaskId, "pending");
        await Assert.ThrowsAsync<SqlException>(
            () => InsertSqlServerTaskAsync(connection, seed, seed.DuplicateTaskId, "failed"));

        await connection.ExecuteAsync(
            """
            DROP TABLE dbo.fn_workflow_recovery_task;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%110_WorkflowRecoveryTask.sql';
            """);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await InsertSqlServerTaskAsync(connection, seed, seed.TaskId, "pending");
        await Assert.ThrowsAsync<SqlException>(
            () => InsertSqlServerTaskAsync(connection, seed, seed.DuplicateTaskId, "dead_lettered"));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    /// <summary>MySQL 删除恢复任务表后必须能重跑 110 并阻止第二条未关闭占用。</summary>
    [TestMethod]
    public async Task MySql_recovers_dropped_recovery_task_table_and_blocks_duplicate_open_occupancy()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var seed = await SeedMySqlAsync(connection);

        await InsertMySqlTaskAsync(connection, seed, seed.TaskId, "pending");
        await Assert.ThrowsAsync<MySqlException>(
            () => InsertMySqlTaskAsync(connection, seed, seed.DuplicateTaskId, "failed"));

        await connection.ExecuteAsync(
            """
            DROP TABLE fn_workflow_recovery_task;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%110_WorkflowRecoveryTask.sql';
            """);

        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        await InsertMySqlTaskAsync(connection, seed, seed.TaskId, "pending");
        await Assert.ThrowsAsync<MySqlException>(
            () => InsertMySqlTaskAsync(connection, seed, seed.DuplicateTaskId, "dead_lettered"));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task<WorkflowSeed> SeedSqlServerAsync(SqlConnection connection)
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
                 @FormVersionId, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);
            """,
            seed);
        return seed;
    }

    private static async Task<WorkflowSeed> SeedMySqlAsync(MySqlConnection connection)
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
                 @FormVersionId, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);
            """,
            seed);
        return seed;
    }

    private static Task<int> InsertSqlServerTaskAsync(
        SqlConnection connection,
        WorkflowSeed seed,
        Guid taskId,
        string statusKey) =>
        connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_workflow_recovery_task
                (Id, TenantId, ScopeKey, TenantScopeKey, InstanceId, StepId, KindKey, StatusKey,
                 AttemptCount, Revision, LeaseOwnerKey, LeaseExpiresAtUtc, LeaseGeneration,
                 NextAttemptAtUtc, LastError, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@TaskId, NULL, 'host', 'host', @InstanceId, NULL, 'stuck_instance', @StatusKey,
                 0, 1, NULL, NULL, 0, NULL, NULL, @Now, @Now);
            """,
            new { TaskId = taskId, seed.InstanceId, StatusKey = statusKey, seed.Now });

    private static Task<int> InsertMySqlTaskAsync(
        MySqlConnection connection,
        WorkflowSeed seed,
        Guid taskId,
        string statusKey) =>
        connection.ExecuteAsync(
            """
            INSERT INTO fn_workflow_recovery_task
                (Id, TenantId, ScopeKey, TenantScopeKey, InstanceId, StepId, KindKey, StatusKey,
                 AttemptCount, Revision, LeaseOwnerKey, LeaseExpiresAtUtc, LeaseGeneration,
                 NextAttemptAtUtc, LastError, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@TaskId, NULL, 'host', 'host', @InstanceId, NULL, 'stuck_instance', @StatusKey,
                 0, 1, NULL, NULL, 0, NULL, NULL, @Now, @Now);
            """,
            new { TaskId = taskId, seed.InstanceId, StatusKey = statusKey, seed.Now });

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

    private sealed record WorkflowSeed(
        Guid DefinitionId,
        Guid DefinitionVersionId,
        Guid FormDefinitionId,
        Guid FormVersionId,
        Guid InstanceId,
        Guid TaskId,
        Guid DuplicateTaskId,
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
                $"rec-{nonce[..8]}",
                "leave.request",
                $"LEAVE-{nonce[..12]}",
                new string('a', 64),
                DateTime.UtcNow);
        }
    }
}
