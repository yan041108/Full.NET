using Dapper;
using DbUp;
using DbUp.Engine;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 112 能按权威实例修订重建存量步骤顺序，并从部分 DDL 状态恢复。</summary>
[TestClass]
public sealed class Migration112WorkflowStepExecutionSequenceRecoveryTests
{
    /// <summary>SQL Server 升级必须重建人审顺序、保持旧写入兼容并恢复缺失索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_backfills_authoritative_sequence_and_recovers_partial_ddl()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await Migration111WorkflowTodoTimeoutPolicyRecoveryTests.MigrateSqlServerThrough111Async(connectionString);
        await using var connection = new SqlConnection(connectionString);
        var seed = await Migration111WorkflowTodoTimeoutPolicyRecoveryTests.SeedSqlServerTodoAsync(connection);
        var activeStepId = Guid.CreateVersion7();
        await PrepareSqlServerLegacyInstanceAsync(connection, seed, activeStepId);

        Assert.AreEqual(1, (await MigrateSqlServerThrough112Async(connectionString)).ExecutedScriptCount);
        Assert.AreEqual(2_000_000L, await connection.ExecuteScalarAsync<long>(
            "SELECT ExecutionSequence FROM dbo.fn_workflow_step WHERE Id = @Id", new { Id = seed.StepId }));
        Assert.AreEqual(3_000_000L, await connection.ExecuteScalarAsync<long>(
            "SELECT ExecutionSequence FROM dbo.fn_workflow_step WHERE Id = @Id", new { Id = activeStepId }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_step') AND name = N'ExecutionSequence'"));

        var unknownStepId = Guid.CreateVersion7();
        await InsertSqlServerUnknownLegacyStepAsync(connection, seed.InstanceId, unknownStepId);
        await connection.ExecuteAsync(
            """
            DROP INDEX IX_fn_workflow_step_Instance_ExecutionSequence ON dbo.fn_workflow_step;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%112_WorkflowStepExecutionSequence.sql';
            """);
        Assert.AreEqual(1, (await MigrateSqlServerThrough112Async(connectionString)).ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_step') AND name = N'IX_fn_workflow_step_Instance_ExecutionSequence'"));
        Assert.IsNull(await connection.ExecuteScalarAsync<long?>(
            "SELECT ExecutionSequence FROM dbo.fn_workflow_step WHERE Id = @Id", new { Id = unknownStepId }));
    }

    /// <summary>MySQL 升级必须重建人审顺序、保持旧写入兼容并恢复缺失索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_backfills_authoritative_sequence_and_recovers_partial_ddl()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await Migration111WorkflowTodoTimeoutPolicyRecoveryTests.MigrateMySqlThrough111Async(connectionString);
        await using var connection = new MySqlConnection(MySqlConnectionStringPolicy.Create(
            connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        var seed = await Migration111WorkflowTodoTimeoutPolicyRecoveryTests.SeedMySqlTodoAsync(connection);
        var activeStepId = Guid.CreateVersion7();
        await PrepareMySqlLegacyInstanceAsync(connection, seed, activeStepId);

        Assert.AreEqual(1, (await MigrateMySqlThrough112Async(connectionString)).ExecutedScriptCount);
        Assert.AreEqual(2_000_000L, await connection.ExecuteScalarAsync<long>(
            "SELECT ExecutionSequence FROM fn_workflow_step WHERE Id = @Id", new { Id = seed.StepId }));
        Assert.AreEqual(3_000_000L, await connection.ExecuteScalarAsync<long>(
            "SELECT ExecutionSequence FROM fn_workflow_step WHERE Id = @Id", new { Id = activeStepId }));
        Assert.AreEqual("YES", await connection.ExecuteScalarAsync<string>(
            "SELECT IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_step' AND COLUMN_NAME = 'ExecutionSequence'"));

        var unknownStepId = Guid.CreateVersion7();
        await InsertMySqlUnknownLegacyStepAsync(connection, seed.InstanceId, unknownStepId);
        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_workflow_step DROP INDEX IX_fn_workflow_step_Instance_ExecutionSequence;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%112_WorkflowStepExecutionSequence.sql';
            """);
        Assert.AreEqual(1, (await MigrateMySqlThrough112Async(connectionString)).ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_step' AND INDEX_NAME = 'IX_fn_workflow_step_Instance_ExecutionSequence'"));
        Assert.IsNull(await connection.ExecuteScalarAsync<long?>(
            "SELECT ExecutionSequence FROM fn_workflow_step WHERE Id = @Id", new { Id = unknownStepId }));
    }

    /// <summary>把 SQL Server 存量实例整理为一个已完成人审和一个活动人审。</summary>
    /// <param name="connection">已打开的 SQL Server 数据库连接。</param>
    /// <param name="seed">迁移测试使用的基础待办数据。</param>
    /// <param name="activeStepId">待创建活动步骤的标识。</param>
    /// <returns>表示异步数据准备过程的任务。</returns>
    private static async Task PrepareSqlServerLegacyInstanceAsync(
        SqlConnection connection,
        Migration111WorkflowTodoTimeoutPolicyRecoveryTests.WorkflowTodoSeed seed,
        Guid activeStepId) => _ = await connection.ExecuteAsync(
        """
        UPDATE dbo.fn_workflow_step SET NodeTypeKey = 'human.approval', StatusKey = 'completed', CompletedAtUtc = @Now WHERE Id = @StepId;
        UPDATE dbo.fn_workflow_instance SET Revision = 2 WHERE Id = @InstanceId;
        INSERT INTO dbo.fn_workflow_action_record
            (Id, InstanceId, StepId, TodoId, ActionKey, ActorUserId, InstanceRevision, IdempotencyKey, CommentSummary, CreatedAtUtc)
        VALUES (@ActionId, @InstanceId, @StepId, @TodoId, 'approve', @ActorUserId, 2, @IdempotencyKey, NULL, @Now);
        INSERT INTO dbo.fn_workflow_step
            (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId, DueAtUtc, AttemptCount, Revision, StartedAtUtc, CompletedAtUtc)
        VALUES (@ActiveStepId, @InstanceId, 'second', 'human.approval', 'active', @ActorUserId, NULL, 0, 1, @Earlier, NULL);
        """,
        LegacyParameters(seed, activeStepId));

    /// <summary>把 MySQL 存量实例整理为一个已完成人审和一个活动人审。</summary>
    /// <param name="connection">已打开的 MySQL 数据库连接。</param>
    /// <param name="seed">迁移测试使用的基础待办数据。</param>
    /// <param name="activeStepId">待创建活动步骤的标识。</param>
    /// <returns>表示异步数据准备过程的任务。</returns>
    private static async Task PrepareMySqlLegacyInstanceAsync(
        MySqlConnection connection,
        Migration111WorkflowTodoTimeoutPolicyRecoveryTests.WorkflowTodoSeed seed,
        Guid activeStepId) => _ = await connection.ExecuteAsync(
        """
        UPDATE fn_workflow_step SET NodeTypeKey = 'human.approval', StatusKey = 'completed', CompletedAtUtc = @Now WHERE Id = @StepId;
        UPDATE fn_workflow_instance SET Revision = 2 WHERE Id = @InstanceId;
        INSERT INTO fn_workflow_action_record
            (Id, InstanceId, StepId, TodoId, ActionKey, ActorUserId, InstanceRevision, IdempotencyKey, CommentSummary, CreatedAtUtc)
        VALUES (@ActionId, @InstanceId, @StepId, @TodoId, 'approve', @ActorUserId, 2, @IdempotencyKey, NULL, @Now);
        INSERT INTO fn_workflow_step
            (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId, DueAtUtc, AttemptCount, Revision, StartedAtUtc, CompletedAtUtc)
        VALUES (@ActiveStepId, @InstanceId, 'second', 'human.approval', 'active', @ActorUserId, NULL, 0, 1, @Earlier, NULL);
        """,
        LegacyParameters(seed, activeStepId));

    /// <summary>构造两库共享的存量步骤参数。</summary>
    /// <param name="seed">迁移测试使用的基础待办数据。</param>
    /// <param name="activeStepId">活动步骤标识。</param>
    /// <returns>可供 Dapper 绑定的存量步骤参数。</returns>
    private static object LegacyParameters(
        Migration111WorkflowTodoTimeoutPolicyRecoveryTests.WorkflowTodoSeed seed,
        Guid activeStepId) => new
        {
            seed.InstanceId,
            seed.StepId,
            seed.TodoId,
            seed.ActorUserId,
            ActiveStepId = activeStepId,
            ActionId = Guid.CreateVersion7(),
            IdempotencyKey = $"approve-{Guid.NewGuid():N}",
            Now = seed.Now.AddMinutes(10),
            Earlier = seed.Now.AddYears(-1),
        };

    /// <summary>模拟滚动期间旧 SQL Server API 写入一条无法证明顺序的完成步骤。</summary>
    /// <param name="connection">已打开的 SQL Server 数据库连接。</param>
    /// <param name="instanceId">流程实例标识。</param>
    /// <param name="stepId">待插入步骤的标识。</param>
    /// <returns>表示异步插入过程的任务。</returns>
    private static async Task InsertSqlServerUnknownLegacyStepAsync(SqlConnection connection, Guid instanceId, Guid stepId) =>
        _ = await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_workflow_step
                (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId, DueAtUtc, AttemptCount, Revision, StartedAtUtc, CompletedAtUtc)
            VALUES (@Id, @InstanceId, 'legacy-unknown', 'human.approval', 'completed', @UserId, NULL, 0, 1, @Now, @Now)
            """,
            new { Id = stepId, InstanceId = instanceId, UserId = Guid.CreateVersion7(), Now = DateTime.UtcNow });

    /// <summary>模拟滚动期间旧 MySQL API 写入一条无法证明顺序的完成步骤。</summary>
    /// <param name="connection">已打开的 MySQL 数据库连接。</param>
    /// <param name="instanceId">流程实例标识。</param>
    /// <param name="stepId">待插入步骤的标识。</param>
    /// <returns>表示异步插入过程的任务。</returns>
    private static async Task InsertMySqlUnknownLegacyStepAsync(MySqlConnection connection, Guid instanceId, Guid stepId) =>
        _ = await connection.ExecuteAsync(
            """
            INSERT INTO fn_workflow_step
                (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId, DueAtUtc, AttemptCount, Revision, StartedAtUtc, CompletedAtUtc)
            VALUES (@Id, @InstanceId, 'legacy-unknown', 'human.approval', 'completed', @UserId, NULL, 0, 1, @Now, @Now)
            """,
            new { Id = stepId, InstanceId = instanceId, UserId = Guid.CreateVersion7(), Now = DateTime.UtcNow });

    /// <summary>只执行 SQL Server 001 至 112。</summary>
    /// <param name="connectionString">目标 SQL Server 数据库连接字符串。</param>
    /// <returns>迁移执行结果。</returns>
    private static Task<MigrationResult> MigrateSqlServerThrough112Async(string connectionString)
    {
        var result = DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal) &&
                    NamingExpandTestMigrationRunner.IsThroughMigration(name, 112))
            .WithVariables(Migration111WorkflowTodoTimeoutPolicyRecoveryTests.MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        return Task.FromResult(Migration111WorkflowTodoTimeoutPolicyRecoveryTests.ToMigrationResult(result));
    }

    /// <summary>只执行 MySQL 001 至 112。</summary>
    /// <param name="connectionString">目标 MySQL 数据库连接字符串。</param>
    /// <returns>迁移执行结果。</returns>
    private static Task<MigrationResult> MigrateMySqlThrough112Async(string connectionString)
    {
        var result = DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(
                connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true))
            .WithPreprocessor(new Migration111WorkflowTodoTimeoutPolicyRecoveryTests.Through111MySqlCompatibilityPreprocessor())
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal) &&
                    NamingExpandTestMigrationRunner.IsThroughMigration(name, 112))
            .WithVariables(Migration111WorkflowTodoTimeoutPolicyRecoveryTests.MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        return Task.FromResult(Migration111WorkflowTodoTimeoutPolicyRecoveryTests.ToMigrationResult(result));
    }
}
