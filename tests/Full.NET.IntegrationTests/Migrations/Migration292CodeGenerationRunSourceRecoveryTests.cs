using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Engine;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>只加载运行表所需的 045/046/051 与 292，验证内联来源、脏数据拒绝和未记账重跑。</summary>
[TestClass]
public sealed class Migration292CodeGenerationRunSourceRecoveryTests
{
    [TestMethod]
    public Task SqlServer_run_sources_recover_without_losing_rows_or_accepting_partial_templates() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_run_sources_recover_without_losing_rows_or_accepting_partial_templates() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var normalized = provider == DatabaseProvider.MySql ? MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true) : connectionString;
        await using DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(normalized) : new MySqlConnection(normalized);
        await connection.OpenAsync();
        Assert.IsTrue(Upgrade(provider, normalized, false).Successful);
        await InsertAsync(connection, "preview", "succeeded", Guid.CreateVersion7(), 1, null);
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(connection, "apply", "running", null, null, null));

        // 旧 CHECK 的 UNKNOWN 曾允许模板标识有值但版本为空；新增约束必须拒绝，且失败不能拆掉原约束。
        var dirty = await InsertAsync(connection, "preview", "succeeded", Guid.CreateVersion7(), null, null);
        Assert.IsFalse(Upgrade(provider, normalized, true).Successful);
        Assert.AreEqual(2, await CountSourceChecksAsync(connection, provider));
        await connection.ExecuteAsync("DELETE FROM fn_codegeneration_run WHERE Id = @Id", new { Id = dirty });
        var upgraded = Upgrade(provider, normalized, true);
        Assert.IsTrue(upgraded.Successful, upgraded.Error?.Message);
        Assert.HasCount(1, upgraded.Scripts);
        var apply = await InsertAsync(connection, "apply", "running", null, null, null);
        Assert.AreEqual(1, await connection.ExecuteAsync("UPDATE fn_codegeneration_run SET Status = 'succeeded' WHERE Id = @Id AND Status = 'running'", new { Id = apply }));
        await InsertAsync(connection, "rollback", "running", null, null, apply);
        await AssertInvalidSourcesAsync(connection);

        foreach (var missing in new[] { 1, 2 })
        {
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "292_CodeGenerationRunSource.sql", provider == DatabaseProvider.SqlServer);
            await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
                ? "ALTER TABLE dbo.fn_codegeneration_run DROP CONSTRAINT CK_fn_codegeneration_run_Template"
                : "ALTER TABLE fn_codegeneration_run DROP CHECK CK_fn_codegeneration_run_Template");
            if (missing == 2) await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
                ? "ALTER TABLE dbo.fn_codegeneration_run DROP CONSTRAINT CK_fn_codegeneration_run_ApplyTemplate"
                : "ALTER TABLE fn_codegeneration_run DROP CHECK CK_fn_codegeneration_run_ApplyTemplate");
            var recovered = Upgrade(provider, normalized, true);
            Assert.IsTrue(recovered.Successful, recovered.Error?.Message);
            Assert.HasCount(1, recovered.Scripts);
            Assert.AreEqual(3, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_codegeneration_run"));
            Assert.AreEqual(2, await CountSourceChecksAsync(connection, provider));
            await AssertInvalidSourcesAsync(connection);
        }
        Assert.HasCount(0, Upgrade(provider, normalized, true).Scripts);
    }

    private static async Task AssertInvalidSourcesAsync(DbConnection connection)
    {
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(connection, "apply", "running", Guid.CreateVersion7(), null, null));
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(connection, "apply", "running", null, 1, null));
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(connection, "apply", "running", Guid.CreateVersion7(), 0, null));
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(connection, "preview", "running", null, null, null));
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(connection, "apply", "running", null, null, Guid.CreateVersion7()));
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(connection, "rollback", "running", null, null, null));
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(connection, "rollback", "running", Guid.CreateVersion7(), 1, Guid.CreateVersion7()));
    }

    private static Task<int> CountSourceChecksAsync(DbConnection connection, DatabaseProvider provider) => connection.QuerySingleAsync<int>(provider == DatabaseProvider.SqlServer
        ? "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.fn_codegeneration_run') AND name IN ('CK_fn_codegeneration_run_Template', 'CK_fn_codegeneration_run_ApplyTemplate')"
        : "SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema = DATABASE() AND table_name = 'fn_codegeneration_run' AND constraint_type = 'CHECK' AND constraint_name IN ('CK_fn_codegeneration_run_Template', 'CK_fn_codegeneration_run_ApplyTemplate')");

    private static async Task<Guid> InsertAsync(DbConnection connection, string kind, string status, Guid? template, long? version, Guid? source)
    {
        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync("""
            INSERT INTO fn_codegeneration_run (Id, TemplateId, TemplateVersion, SourceApplyRunId, OperationKind, Status,
                ModuleKey, EntityKey, SchemaSha256, ArtifactCount, ManifestSha256, ErrorCode, RequestedByUserId, StartedAtUtc, FinishedAtUtc)
            VALUES (@Id, @Template, @Version, @Source, @Kind, @Status, 'catalog', 'source_probe', @Hash, 1, @Hash, NULL, @Actor, @Now, @Now)
            """, new { Id = id, Template = template, Version = version, Source = source, Kind = kind, Status = status,
                Hash = new string('a', 64), Actor = Guid.CreateVersion7(), Now = DateTime.UtcNow });
        return id;
    }

    private static DatabaseUpgradeResult Upgrade(DatabaseProvider provider, string connection, bool includeTarget)
    {
        var builder = provider == DatabaseProvider.SqlServer ? DeployChanges.To.SqlDatabase(connection) : DeployChanges.To.MySqlDatabase(connection);
        // 不随未来迁移扩围；恢复场景须确保 292 尚未记账且运行表处于明确历史边界。
        return builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name =>
                name.Contains(".Migrations." + provider + ".", StringComparison.Ordinal)
                && (name.EndsWith(".045_CodeGenerationRun.sql", StringComparison.Ordinal)
                    || name.EndsWith(".046_CodeGenerationApply.sql", StringComparison.Ordinal)
                    || name.EndsWith(".051_CodeGenerationRollback.sql", StringComparison.Ordinal)
                    || includeTarget && name.EndsWith(".292_CodeGenerationRunSource.sql", StringComparison.Ordinal)))
            .WithExecutionTimeout(TimeSpan.FromSeconds(120)).Build().PerformUpgrade();
    }
}
