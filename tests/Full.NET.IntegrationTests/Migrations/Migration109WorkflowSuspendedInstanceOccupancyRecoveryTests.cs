using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 109 把暂停实例纳入业务唯一占用，并能从部分 DDL 未记账状态恢复。</summary>
[TestClass]
public sealed class Migration109WorkflowSuspendedInstanceOccupancyRecoveryTests
{
    /// <summary>SQL Server 在计算列被回退为仅 active 后必须能重跑 109 并阻止同业务第二实例。</summary>
    [TestMethod]
    public async Task SqlServer_recovers_partial_occupancy_ddl_and_blocks_second_business_instance()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);
        var seed = await SeedSqlServerWorkflowAsync(connection);

        await connection.ExecuteAsync(
            "UPDATE dbo.fn_workflow_instance SET StatusKey = 'suspended' WHERE Id = @InstanceId",
            seed);
        await Assert.ThrowsAsync<SqlException>(() => InsertSqlServerActiveInstanceAsync(connection, seed));

        await connection.ExecuteAsync(
            """
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_instance')
                  AND name = N'UX_fn_workflow_instance_ActiveBusinessKey')
                DROP INDEX UX_fn_workflow_instance_ActiveBusinessKey ON dbo.fn_workflow_instance;
            ALTER TABLE dbo.fn_workflow_instance DROP COLUMN ActiveBusinessKey;
            ALTER TABLE dbo.fn_workflow_instance ADD
                ActiveBusinessKey AS (CASE WHEN StatusKey = 'active' THEN CONCAT(TenantScopeKey, N'|', BusinessType, N'|', BusinessId) END) PERSISTED;
            CREATE UNIQUE INDEX UX_fn_workflow_instance_ActiveBusinessKey
                ON dbo.fn_workflow_instance(ActiveBusinessKey) WHERE StatusKey = 'active';
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%109_WorkflowSuspendedInstanceOccupancy.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(
            (await connection.ExecuteScalarAsync<string>(
                """
                SELECT definition
                FROM sys.computed_columns
                WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_instance')
                  AND name = N'ActiveBusinessKey'
                """) ?? string.Empty)
            .Contains("suspended", StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<SqlException>(() => InsertSqlServerActiveInstanceAsync(connection, seed));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    /// <summary>MySQL 在 generated column 被回退为仅 active 后必须能重跑 109 并阻止同业务第二实例。</summary>
    [TestMethod]
    public async Task MySql_recovers_partial_occupancy_ddl_and_blocks_second_business_instance()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var seed = await SeedMySqlWorkflowAsync(connection);

        await connection.ExecuteAsync(
            "UPDATE fn_workflow_instance SET StatusKey = 'suspended' WHERE Id = @InstanceId",
            seed);
        await Assert.ThrowsAsync<MySqlException>(() => InsertMySqlActiveInstanceAsync(connection, seed));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_instance DROP INDEX UX_fn_workflow_instance_ActiveBusinessKey;
            ALTER TABLE fn_workflow_instance
                MODIFY COLUMN ActiveBusinessKey varchar(258) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin
                    GENERATED ALWAYS AS (
                        CASE WHEN StatusKey = 'active'
                            THEN CONCAT(TenantScopeKey, '|', BusinessType, '|', BusinessId)
                            ELSE NULL END) STORED;
            ALTER TABLE fn_workflow_instance
                ADD CONSTRAINT UX_fn_workflow_instance_ActiveBusinessKey UNIQUE (ActiveBusinessKey);
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%109_WorkflowSuspendedInstanceOccupancy.sql';
            """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.IsTrue(
            (await connection.ExecuteScalarAsync<string>(
                """
                SELECT GENERATION_EXPRESSION
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_workflow_instance'
                  AND COLUMN_NAME = 'ActiveBusinessKey'
                """) ?? string.Empty)
            .Contains("suspended", StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<MySqlException>(() => InsertMySqlActiveInstanceAsync(connection, seed));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    /// <summary>写入最小可暂停工作流实例，供占用约束断言使用。</summary>
    /// <param name="connection">SQL Server 连接。</param>
    /// <returns>已写入的种子标识。</returns>
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
                 @FormVersionId, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);
            """,
            seed);
        return seed;
    }

    /// <summary>写入最小可暂停工作流实例，供占用约束断言使用。</summary>
    /// <param name="connection">MySQL 连接。</param>
    /// <returns>已写入的种子标识。</returns>
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
                 @FormVersionId, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);
            """,
            seed);
        return seed;
    }

    /// <summary>尝试插入占用同一业务键的运行中实例。</summary>
    /// <param name="connection">SQL Server 连接。</param>
    /// <param name="seed">已存在的暂停实例种子。</param>
    /// <returns>受影响行数。</returns>
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
                 @FormVersionId, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);
            """,
            seed);

    /// <summary>尝试插入占用同一业务键的运行中实例。</summary>
    /// <param name="connection">MySQL 连接。</param>
    /// <param name="seed">已存在的暂停实例种子。</param>
    /// <returns>受影响行数。</returns>
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
                 @FormVersionId, @BusinessType, @BusinessId, 'active', 1,
                 @ActorUserId, @Now);
            """,
            seed);

    /// <summary>创建双库迁移运行器。</summary>
    /// <param name="provider">数据库提供程序。</param>
    /// <param name="connectionString">连接字符串。</param>
    /// <returns>可重复执行的 DbUp 运行器。</returns>
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

    /// <summary>109 占用恢复测试使用的最小工作流种子。</summary>
    /// <param name="DefinitionId">流程定义标识。</param>
    /// <param name="DefinitionVersionId">已发布定义版本标识。</param>
    /// <param name="FormDefinitionId">表单定义标识。</param>
    /// <param name="FormVersionId">已发布表单版本标识。</param>
    /// <param name="InstanceId">已存在实例标识。</param>
    /// <param name="NextInstanceId">用于冲突插入的第二实例标识。</param>
    /// <param name="ActorUserId">发起人标识。</param>
    /// <param name="DefinitionKey">定义键。</param>
    /// <param name="BusinessType">业务类型。</param>
    /// <param name="BusinessId">业务标识。</param>
    /// <param name="ContentHash">发布内容摘要。</param>
    /// <param name="Now">种子时间。</param>
    private sealed record WorkflowSeed(
        Guid DefinitionId,
        Guid DefinitionVersionId,
        Guid FormDefinitionId,
        Guid FormVersionId,
        Guid InstanceId,
        Guid NextInstanceId,
        Guid ActorUserId,
        string DefinitionKey,
        string BusinessType,
        string BusinessId,
        string ContentHash,
        DateTime Now)
    {
        /// <summary>生成互不冲突的 UUID v7 种子。</summary>
        /// <returns>可用于双库插入的种子。</returns>
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
                $"occ-{nonce[..8]}",
                "leave.request",
                $"LEAVE-{nonce[..12]}",
                new string('a', 64),
                DateTime.UtcNow);
        }
    }
}
