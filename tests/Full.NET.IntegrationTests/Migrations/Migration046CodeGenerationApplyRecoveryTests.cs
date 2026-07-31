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
/// 验证 046 无损扩展 045，并只允许 Apply 从 running 单向收敛到终态。
/// </summary>
[TestClass]
public sealed class Migration046CodeGenerationApplyRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_apply_migration_preserves_preview_and_guards_completion()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        var runId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_codegeneration_run
                (Id, TemplateId, TemplateVersion, OperationKind, Status,
                 ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
                 ManifestSha256, ErrorCode, RequestedByUserId,
                 StartedAtUtc, FinishedAtUtc)
            VALUES
                (@Id, NEWID(), 1, 'apply', 'running',
                 'catalog', 'product', REPLICATE('a', 64), 8,
                 REPLICATE('b', 64), NULL, NEWID(),
                 SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """,
            new { Id = runId });
        Assert.AreEqual(1, await connection.ExecuteAsync(
            """
            UPDATE dbo.fn_codegeneration_run
            SET Status = 'succeeded', FinishedAtUtc = SYSDATETIMEOFFSET()
            WHERE Id = @Id AND OperationKind = 'apply' AND Status = 'running'
            """,
            new { Id = runId }));
        Assert.AreEqual(0, await connection.ExecuteAsync(
            """
            UPDATE dbo.fn_codegeneration_run
            SET Status = 'failed', ModuleKey = NULL, EntityKey = NULL,
                SchemaSha256 = NULL, ArtifactCount = 0,
                ManifestSha256 = NULL, ErrorCode = 'codegen.run.apply_failed'
            WHERE Id = @Id AND OperationKind = 'apply' AND Status = 'running'
            """,
            new { Id = runId }));

        await connection.ExecuteAsync(
            """
            DELETE FROM dbo.SchemaVersions
            WHERE ScriptName LIKE '%046_CodeGenerationApply.sql'
            """);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual("succeeded", await connection.ExecuteScalarAsync<string>(
            "SELECT Status FROM dbo.fn_codegeneration_run WHERE Id = @Id",
            new { Id = runId }));
    }

    [TestMethod]
    public async Task MySql_apply_migration_preserves_preview_and_guards_completion()
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
        var runId = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_codegeneration_run
                (Id, TemplateId, TemplateVersion, OperationKind, Status,
                 ModuleKey, EntityKey, SchemaSha256, ArtifactCount,
                 ManifestSha256, ErrorCode, RequestedByUserId,
                 StartedAtUtc, FinishedAtUtc)
            VALUES
                (@Id, UNHEX(REPLACE(UUID(), '-', '')), 1, 'apply', 'running',
                 'catalog', 'product', REPEAT('a', 64), 8,
                 REPEAT('b', 64), NULL, UNHEX(REPLACE(UUID(), '-', '')),
                 UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            """,
            new { Id = runId });
        Assert.AreEqual(1, await connection.ExecuteAsync(
            """
            UPDATE fn_codegeneration_run
            SET Status = 'succeeded', FinishedAtUtc = UTC_TIMESTAMP(6)
            WHERE Id = @Id AND OperationKind = 'apply' AND Status = 'running'
            """,
            new { Id = runId }));
        Assert.AreEqual(0, await connection.ExecuteAsync(
            """
            UPDATE fn_codegeneration_run
            SET Status = 'failed', ModuleKey = NULL, EntityKey = NULL,
                SchemaSha256 = NULL, ArtifactCount = 0,
                ManifestSha256 = NULL, ErrorCode = 'codegen.run.apply_failed'
            WHERE Id = @Id AND OperationKind = 'apply' AND Status = 'running'
            """,
            new { Id = runId }));

        await connection.ExecuteAsync(
            """
            DELETE FROM schemaversions
            WHERE ScriptName LIKE '%046_CodeGenerationApply.sql'
            """);
        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual("succeeded", await connection.ExecuteScalarAsync<string>(
            "SELECT Status FROM fn_codegeneration_run WHERE Id = @Id",
            new { Id = runId }));
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
