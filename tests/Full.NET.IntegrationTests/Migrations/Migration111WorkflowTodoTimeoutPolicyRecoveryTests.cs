using DbUp;
using DbUp.Engine;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using System.Text.RegularExpressions;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 111 待办超时调度结构可从部分 DDL 状态恢复。</summary>
[TestClass]
public sealed partial class Migration111WorkflowTodoTimeoutPolicyRecoveryTests
{
    /// <summary>SQL Server 丢失信号列与扫描索引后，重跑 111 必须完整恢复且保持幂等。</summary>
    [TestMethod]
    public async Task SqlServer_recovers_missing_timeout_signal_column_and_scan_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await MigrateSqlServerThrough111Async(connectionString);
        await using var connection = new SqlConnection(connectionString);
        var seed = await SeedSqlServerTodoAsync(connection);
        var dueAtUtcBeforeRecovery = await connection.ExecuteScalarAsync<DateTime>(
            "SELECT DueAtUtc FROM dbo.fn_workflow_todo WHERE Id = @TodoId;",
            seed);

        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_workflow_todo_TimeoutScan ON dbo.fn_workflow_todo;
            ALTER TABLE dbo.fn_workflow_todo DROP COLUMN NextTimeoutSignalAtUtc;
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%111_WorkflowTodoTimeoutPolicy.sql';
            """);

        var recovered = await MigrateSqlServerThrough111Async(connectionString);
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_todo') AND name = N'NextTimeoutSignalAtUtc';"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_todo') AND name = N'IX_fn_workflow_todo_TimeoutScan';"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual("pending", await connection.ExecuteScalarAsync<string>(
            "SELECT StatusKey FROM dbo.fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual(7L, await connection.ExecuteScalarAsync<long>(
            "SELECT Revision FROM dbo.fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT ReminderCount FROM dbo.fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual(dueAtUtcBeforeRecovery, await connection.ExecuteScalarAsync<DateTime>(
            "SELECT DueAtUtc FROM dbo.fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual(0, (await MigrateSqlServerThrough111Async(connectionString)).ExecutedScriptCount);
    }

    /// <summary>MySQL 丢失信号列与扫描索引后，重跑 111 必须完整恢复且保持幂等。</summary>
    [TestMethod]
    public async Task MySql_recovers_missing_timeout_signal_column_and_scan_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await MigrateMySqlThrough111Async(connectionString);
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var seed = await SeedMySqlTodoAsync(connection);
        var dueAtUtcBeforeRecovery = await connection.ExecuteScalarAsync<DateTime>(
            "SELECT DueAtUtc FROM fn_workflow_todo WHERE Id = @TodoId;",
            seed);

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_todo DROP INDEX IX_fn_workflow_todo_TimeoutScan;
            ALTER TABLE fn_workflow_todo DROP COLUMN NextTimeoutSignalAtUtc;
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%111_WorkflowTodoTimeoutPolicy.sql';
            """);

        var recovered = await MigrateMySqlThrough111Async(connectionString);
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'NextTimeoutSignalAtUtc';"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND INDEX_NAME = 'IX_fn_workflow_todo_TimeoutScan';"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual("pending", await connection.ExecuteScalarAsync<string>(
            "SELECT StatusKey FROM fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual(7L, await connection.ExecuteScalarAsync<long>(
            "SELECT Revision FROM fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT ReminderCount FROM fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual(dueAtUtcBeforeRecovery, await connection.ExecuteScalarAsync<DateTime>(
            "SELECT DueAtUtc FROM fn_workflow_todo WHERE Id = @TodoId;", seed));
        Assert.AreEqual(0, (await MigrateMySqlThrough111Async(connectionString)).ExecutedScriptCount);
    }

    /// <summary>插入一条带超时状态的 SQL Server 待办，用于确认部分恢复不破坏目标表业务数据。</summary>
    /// <param name="connection">测试数据库连接。</param>
    /// <returns>已写入的完整工作流种子。</returns>
    private static async Task<WorkflowTodoSeed> SeedSqlServerTodoAsync(SqlConnection connection)
    {
        var seed = WorkflowTodoSeed.Create();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_workflow_definition
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionKey, DraftId,
                 LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES (@DefinitionId, NULL, 'host', 'host', @DefinitionKey, NULL, NULL, @ActorUserId, @Now, NULL, 1);

            INSERT INTO dbo.fn_workflow_form_definition
                (Id, TenantId, ScopeKey, TenantScopeKey, FormKey, DraftSchemaJson,
                 DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@FormDefinitionId, NULL, 'host', 'host', @DefinitionKey, N'{}',
                    1, @FormVersionId, @ActorUserId, @Now, NULL);

            INSERT INTO dbo.fn_workflow_form_version
                (Id, FormDefinitionId, VersionNumber, SchemaVersion, AdapterVersion,
                 ComponentCatalogVersion, FormSchemaJson, WebRenderSchemaJson,
                 ContentHash, PublishedById, PublishedAtUtc)
            VALUES (@FormVersionId, @FormDefinitionId, 1, 1, 1, 1, N'{}', N'{}',
                    @ContentHash, @ActorUserId, @Now);

            INSERT INTO dbo.fn_workflow_definition_version
                (Id, DefinitionId, FormVersionId, VersionNumber, SchemaVersion, CanonicalJson,
                 ContentHash, PublishedById, PublishedAtUtc)
            VALUES (@DefinitionVersionId, @DefinitionId, @FormVersionId, 1, 1, N'{}',
                    @ContentHash, @ActorUserId, @Now);

            INSERT INTO dbo.fn_workflow_instance
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionVersionId,
                 FormVersionId, BusinessType, BusinessId, StatusKey, Revision,
                 StartedById, StartedAtUtc)
            VALUES (@InstanceId, NULL, 'host', 'host', @DefinitionVersionId,
                    @FormVersionId, @BusinessType, @BusinessId, 'active', 1,
                    @ActorUserId, @Now);

            INSERT INTO dbo.fn_workflow_step
                (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId,
                 DueAtUtc, AttemptCount, Revision, StartedAtUtc, CompletedAtUtc)
            VALUES (@StepId, @InstanceId, 'approve', 'approval', 'active', @ActorUserId,
                    @DueAtUtc, 0, 1, @Now, NULL);

            INSERT INTO dbo.fn_workflow_todo
                (Id, InstanceId, StepId, AssigneeUserId, StatusKey, Revision,
                 ArrivedAtUtc, CompletedAtUtc, ResultActionKey, DueAtUtc,
                 NextReminderAtUtc, EscalateAtUtc, MaxReminderCount,
                 ReminderIntervalMinutes, ReminderCount, EscalationRecipientUserId,
                 LastReminderAtUtc, EscalatedAtUtc, NextTimeoutSignalAtUtc)
            VALUES (@TodoId, @InstanceId, @StepId, @ActorUserId, 'pending', 7,
                    @Now, NULL, NULL, @DueAtUtc, @DueAtUtc, @EscalateAtUtc, 3,
                    15, 1, @EscalationRecipientUserId, @Now, NULL, @DueAtUtc);
            """,
            seed);
        return seed;
    }

    /// <summary>插入一条带超时状态的 MySQL 待办，用于确认部分恢复不破坏目标表业务数据。</summary>
    /// <param name="connection">测试数据库连接。</param>
    /// <returns>已写入的完整工作流种子。</returns>
    private static async Task<WorkflowTodoSeed> SeedMySqlTodoAsync(MySqlConnection connection)
    {
        var seed = WorkflowTodoSeed.Create();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_workflow_definition
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionKey, DraftId,
                 LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES (@DefinitionId, NULL, 'host', 'host', @DefinitionKey, NULL, NULL, @ActorUserId, @Now, NULL, 1);

            INSERT INTO fn_workflow_form_definition
                (Id, TenantId, ScopeKey, TenantScopeKey, FormKey, DraftSchemaJson,
                 DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@FormDefinitionId, NULL, 'host', 'host', @DefinitionKey, '{}',
                    1, @FormVersionId, @ActorUserId, @Now, NULL);

            INSERT INTO fn_workflow_form_version
                (Id, FormDefinitionId, VersionNumber, SchemaVersion, AdapterVersion,
                 ComponentCatalogVersion, FormSchemaJson, WebRenderSchemaJson,
                 ContentHash, PublishedById, PublishedAtUtc)
            VALUES (@FormVersionId, @FormDefinitionId, 1, 1, 1, 1, '{}', '{}',
                    @ContentHash, @ActorUserId, @Now);

            INSERT INTO fn_workflow_definition_version
                (Id, DefinitionId, FormVersionId, VersionNumber, SchemaVersion, CanonicalJson,
                 ContentHash, PublishedById, PublishedAtUtc)
            VALUES (@DefinitionVersionId, @DefinitionId, @FormVersionId, 1, 1, '{}',
                    @ContentHash, @ActorUserId, @Now);

            INSERT INTO fn_workflow_instance
                (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionVersionId,
                 FormVersionId, BusinessType, BusinessId, StatusKey, Revision,
                 StartedById, StartedAtUtc)
            VALUES (@InstanceId, NULL, 'host', 'host', @DefinitionVersionId,
                    @FormVersionId, @BusinessType, @BusinessId, 'active', 1,
                    @ActorUserId, @Now);

            INSERT INTO fn_workflow_step
                (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId,
                 DueAtUtc, AttemptCount, Revision, StartedAtUtc, CompletedAtUtc)
            VALUES (@StepId, @InstanceId, 'approve', 'approval', 'active', @ActorUserId,
                    @DueAtUtc, 0, 1, @Now, NULL);

            INSERT INTO fn_workflow_todo
                (Id, InstanceId, StepId, AssigneeUserId, StatusKey, Revision,
                 ArrivedAtUtc, CompletedAtUtc, ResultActionKey, DueAtUtc,
                 NextReminderAtUtc, EscalateAtUtc, MaxReminderCount,
                 ReminderIntervalMinutes, ReminderCount, EscalationRecipientUserId,
                 LastReminderAtUtc, EscalatedAtUtc, NextTimeoutSignalAtUtc)
            VALUES (@TodoId, @InstanceId, @StepId, @ActorUserId, 'pending', 7,
                    @Now, NULL, NULL, @DueAtUtc, @DueAtUtc, @EscalateAtUtc, 3,
                    15, 1, @EscalationRecipientUserId, @Now, NULL, @DueAtUtc);
            """,
            seed);
        return seed;
    }

    /// <summary>描述迁移恢复测试所需的完整工作流待办依赖链。</summary>
    /// <param name="DefinitionId">流程定义标识。</param>
    /// <param name="DefinitionVersionId">流程定义版本标识。</param>
    /// <param name="FormDefinitionId">表单定义标识。</param>
    /// <param name="FormVersionId">表单版本标识。</param>
    /// <param name="InstanceId">流程实例标识。</param>
    /// <param name="StepId">流程步骤标识。</param>
    /// <param name="TodoId">待办标识。</param>
    /// <param name="ActorUserId">操作人标识。</param>
    /// <param name="EscalationRecipientUserId">升级接收人标识。</param>
    /// <param name="DefinitionKey">流程定义键。</param>
    /// <param name="BusinessType">业务类型。</param>
    /// <param name="BusinessId">业务标识。</param>
    /// <param name="ContentHash">发布内容哈希。</param>
    /// <param name="Now">创建时间。</param>
    /// <param name="DueAtUtc">逾期时间。</param>
    /// <param name="EscalateAtUtc">升级时间。</param>
    private sealed record WorkflowTodoSeed(
        Guid DefinitionId,
        Guid DefinitionVersionId,
        Guid FormDefinitionId,
        Guid FormVersionId,
        Guid InstanceId,
        Guid StepId,
        Guid TodoId,
        Guid ActorUserId,
        Guid EscalationRecipientUserId,
        string DefinitionKey,
        string BusinessType,
        string BusinessId,
        string ContentHash,
        DateTime Now,
        DateTime DueAtUtc,
        DateTime EscalateAtUtc)
    {
        /// <summary>创建互不冲突且时间关系确定的工作流待办种子。</summary>
        /// <returns>新建的种子。</returns>
        public static WorkflowTodoSeed Create()
        {
            var now = DateTime.UtcNow;
            var nonce = Guid.CreateVersion7().ToString("N");
            return new WorkflowTodoSeed(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                $"timeout-{nonce}",
                "integration-test",
                nonce,
                new string('a', 64),
                now,
                now.AddMinutes(30),
                now.AddMinutes(60));
        }
    }

    /// <summary>只执行 SQL Server 001 至 111，防止未来迁移改变本恢复用例语义。</summary>
    /// <param name="connectionString">隔离测试数据库连接串。</param>
    /// <returns>本轮迁移结果。</returns>
    private static Task<MigrationResult> MigrateSqlServerThrough111Async(string connectionString)
    {
        var result = DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal) &&
                    NamingExpandTestMigrationRunner.IsThroughMigration(name, 111))
            .WithVariables(MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        return Task.FromResult(ToMigrationResult(result));
    }

    /// <summary>只执行 MySQL 001 至 111，防止未来迁移改变本恢复用例语义。</summary>
    /// <param name="connectionString">隔离测试数据库连接串。</param>
    /// <returns>本轮迁移结果。</returns>
    private static Task<MigrationResult> MigrateMySqlThrough111Async(string connectionString)
    {
        var result = DeployChanges.To.MySqlDatabase(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: true))
            .WithPreprocessor(new Through111MySqlCompatibilityPreprocessor())
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal) &&
                    NamingExpandTestMigrationRunner.IsThroughMigration(name, 111))
            .WithVariables(MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        return Task.FromResult(ToMigrationResult(result));
    }

    /// <summary>提供历史破坏性 Contract 迁移所需的测试维护证据。</summary>
    /// <returns>DbUp 变量字典。</returns>
    private static Dictionary<string, string> MigrationVariables() =>
        new(StringComparer.Ordinal)
        {
            ["UuidContractMaintenanceMode"] = "1",
            ["UuidContractBackupVerified"] = "1",
            ["UuidContractLegacyWritersStopped"] = "1",
            ["UuidContractDestructiveDdlApprovalId"] = "test-uuid-contract-009",
            ["PreV1NamingContractMaintenanceMode"] = "1",
            ["PreV1NamingContractBackupVerified"] = "1",
            ["PreV1NamingContractLegacyWritersStopped"] = "1",
            ["PreV1NamingContractLegacyOutboxDrained"] = "1",
            ["PreV1NamingContractDestructiveDdlApprovalId"] = MigrationContractOptionFactory.NamingApprovalId,
        };

    /// <summary>把 DbUp 结果转换为项目迁移结果并保留原始失败。</summary>
    /// <param name="result">DbUp 执行结果。</param>
    /// <returns>成功迁移结果。</returns>
    private static MigrationResult ToMigrationResult(DbUp.Engine.DatabaseUpgradeResult result)
    {
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        return new MigrationResult(true, result.Scripts.Count());
    }

    /// <summary>仅在 Through111 测试运行时兼容已发布 094 的 MySQL 8 不支持语法。</summary>
    private sealed partial class Through111MySqlCompatibilityPreprocessor : IScriptPreprocessor
    {
        /// <summary>移除由 095 幂等补齐的 094 条件约束语句。</summary>
        /// <param name="contents">原始迁移脚本。</param>
        /// <returns>兼容 MySQL 8 的脚本。</returns>
        public string Process(string contents)
        {
            ArgumentNullException.ThrowIfNull(contents);
            return UnsupportedConstraintSyntax().Replace(
                contents,
                "-- 094 compatibility: constraints converge in migration 095.\n");
        }

        /// <summary>匹配 094 中由 095 接管的三条不兼容约束语句。</summary>
        /// <returns>已编译的闭合正则表达式。</returns>
        [GeneratedRegex(
            @"(?ms)^\s*ALTER\s+TABLE\s+fn_messaging_stream_ownership\s+ADD\s+CONSTRAINT\s+IF\s+NOT\s+EXISTS\s+CK_fn_messaging_stream_ownership_(?:SchemaVersion|CurrentOwner|PreviousOwner)\s+CHECK\s*\([^;]+;\s*")]
        private static partial Regex UnsupportedConstraintSyntax();
    }
}
