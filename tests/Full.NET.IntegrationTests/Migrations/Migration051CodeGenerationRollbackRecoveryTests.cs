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
/// 验证 051 扩展 rollback 操作态，并保证成功 Rollback 对同一 Apply 唯一。
/// </summary>
[TestClass]
public sealed class Migration051CodeGenerationRollbackRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_rollback_migration_guards_completion_and_unique_success()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var applyRunId = Guid.NewGuid();
        var rollbackRunId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_codegeneration_run
                (Id, TemplateId, TemplateVersion, OperationKind, Status,
                 ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
                 ManifestSha256, ErrorCode, RequestedByUserId,
                 StartedAtUtc, FinishedAtUtc, SourceApplyRunId)
            VALUES
                (@ApplyId, NEWID(), 1, 'apply', 'succeeded',
                 'catalog', 'product', REPLICATE('a', 64), 8,
                 REPLICATE('b', 64), NULL, NEWID(),
                 SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NULL),
                (@RollbackId, NULL, NULL, 'rollback', 'running',
                 'catalog', 'product', REPLICATE('a', 64), 0,
                 REPLICATE('c', 64), NULL, NEWID(),
                 SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), @ApplyId)
            """,
            new { ApplyId = applyRunId, RollbackId = rollbackRunId });

        Assert.AreEqual(1, await connection.ExecuteAsync(
            """
            UPDATE dbo.fn_codegeneration_run
            SET Status = 'succeeded', FinishedAtUtc = SYSDATETIMEOFFSET()
            WHERE Id = @Id AND OperationKind = 'rollback' AND Status = 'running'
            """,
            new { Id = rollbackRunId }));
        Assert.AreEqual(0, await connection.ExecuteAsync(
            """
            UPDATE dbo.fn_codegeneration_run
            SET Status = 'failed', ModuleKey = NULL, EntityKey = NULL,
                SchemaSha256 = NULL, ArtifactCount = 0,
                ManifestSha256 = NULL, ErrorCode = 'codegen.run.rollback_failed'
            WHERE Id = @Id AND OperationKind = 'rollback' AND Status = 'running'
            """,
            new { Id = rollbackRunId }));

        try
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_codegeneration_run
                    (Id, TemplateId, TemplateVersion, OperationKind, Status,
                     ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
                     ManifestSha256, ErrorCode, RequestedByUserId,
                     StartedAtUtc, FinishedAtUtc, SourceApplyRunId)
                VALUES
                    (NEWID(), NULL, NULL, 'rollback', 'succeeded',
                     'catalog', 'product', REPLICATE('a', 64), 0,
                     REPLICATE('d', 64), NULL, NEWID(),
                     SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), @ApplyId)
                """,
                new { ApplyId = applyRunId });
            Assert.Fail("Expected duplicate succeeded rollback to violate unique constraint.");
        }
        catch (SqlException exception)
        {
            StringAssert.Contains(
                exception.Message,
                "UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId");
        }

        await connection.ExecuteAsync(
            """
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%051_CodeGenerationRollback.sql'
            """);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual("succeeded", await connection.ExecuteScalarAsync<string>(
            "SELECT Status FROM dbo.fn_codegeneration_run WHERE Id = @Id",
            new { Id = rollbackRunId }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.fn_codegeneration_run')
              AND name = N'SourceApplyRunId'
            """));
    }

    [TestMethod]
    public async Task MySql_rollback_migration_guards_completion_and_unique_success()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var applyRunId = Guid.NewGuid();
        var rollbackRunId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_codegeneration_run
                (Id, TemplateId, TemplateVersion, OperationKind, Status,
                 ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
                 ManifestSha256, ErrorCode, RequestedByUserId,
                 StartedAtUtc, FinishedAtUtc, SourceApplyRunId)
            VALUES
                (@ApplyId, UNHEX(REPLACE(UUID(), '-', '')), 1, 'apply', 'succeeded',
                 'catalog', 'product', REPEAT('a', 64), 8,
                 REPEAT('b', 64), NULL, UNHEX(REPLACE(UUID(), '-', '')),
                 UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), NULL),
                (@RollbackId, NULL, NULL, 'rollback', 'running',
                 'catalog', 'product', REPEAT('a', 64), 0,
                 REPEAT('c', 64), NULL, UNHEX(REPLACE(UUID(), '-', '')),
                 UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @ApplyId)
            """,
            new { ApplyId = applyRunId, RollbackId = rollbackRunId });

        Assert.AreEqual(1, await connection.ExecuteAsync(
            """
            UPDATE fn_codegeneration_run
            SET Status = 'succeeded', FinishedAtUtc = UTC_TIMESTAMP(6)
            WHERE Id = @Id AND OperationKind = 'rollback' AND Status = 'running'
            """,
            new { Id = rollbackRunId }));
        Assert.AreEqual(0, await connection.ExecuteAsync(
            """
            UPDATE fn_codegeneration_run
            SET Status = 'failed', ModuleKey = NULL, EntityKey = NULL,
                SchemaSha256 = NULL, ArtifactCount = 0,
                ManifestSha256 = NULL, ErrorCode = 'codegen.run.rollback_failed'
            WHERE Id = @Id AND OperationKind = 'rollback' AND Status = 'running'
            """,
            new { Id = rollbackRunId }));

        try
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO fn_codegeneration_run
                    (Id, TemplateId, TemplateVersion, OperationKind, Status,
                     ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
                     ManifestSha256, ErrorCode, RequestedByUserId,
                     StartedAtUtc, FinishedAtUtc, SourceApplyRunId)
                VALUES
                    (UNHEX(REPLACE(UUID(), '-', '')), NULL, NULL, 'rollback', 'succeeded',
                     'catalog', 'product', REPEAT('a', 64), 0,
                     REPEAT('d', 64), NULL, UNHEX(REPLACE(UUID(), '-', '')),
                     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @ApplyId)
                """,
                new { ApplyId = applyRunId });
            Assert.Fail("Expected duplicate succeeded rollback to violate unique constraint.");
        }
        catch (MySqlException exception)
        {
            StringAssert.Contains(
                exception.Message,
                "UX_fn_codegeneration_run_SucceededRollbackSourceApplyRunId");
        }

        await connection.ExecuteAsync(
            """
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%051_CodeGenerationRollback.sql'
            """);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual("succeeded", await connection.ExecuteScalarAsync<string>(
            "SELECT Status FROM fn_codegeneration_run WHERE Id = @Id",
            new { Id = rollbackRunId }));
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