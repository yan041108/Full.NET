using System.Data.Common;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>取消状态约束在旧结构、缺约束和已展开但未记账时可恢复，并保留历史任务。</summary>
[TestClass]
public sealed class Migration253ImportExportTaskCancellationTests
{
    [TestMethod]
    public Task SqlServer_constraint_recovers_unjournaled_phases() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_constraint_recovers_unjournaled_phases() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var scope = new Through253Scope(provider, cs);
        await scope.Runner.MigrateAsync();
        await using DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await connection.OpenAsync();
        var id = Guid.CreateVersion7(); var now = DateTime.UtcNow;
        await connection.ExecuteAsync("""
            INSERT INTO fn_import_export_task (Id,TenantId,SchemaKey,SchemaDisplayName,WorksheetKey,SourceFileId,SourceFileName,
                StatusKey,TotalRows,ValidRowCount,InvalidRowCount,RequestedByUserId,CreatedAtUtc,Version,
                ProcessedRowCount,SucceededRowCount,ExecutionFailedRowCount,NextLineNumber)
            VALUES (@Id,@TenantId,'positions','positions','positions',@FileId,'preserved.xlsx','queued',3,3,0,@Actor,@Now,7,1,1,0,1)
            """, new { Id = id, TenantId = Guid.CreateVersion7(), FileId = Guid.CreateVersion7(), Actor = Guid.CreateVersion7(), Now = now });
        var drop = provider == DatabaseProvider.SqlServer ? "ALTER TABLE dbo.fn_import_export_task DROP CONSTRAINT CK_fn_import_export_task_StatusKey"
            : "ALTER TABLE fn_import_export_task DROP CHECK CK_fn_import_export_task_StatusKey";
        foreach (var phase in new[] { "legacy", "missing", "expanded" })
        {
            if (phase != "expanded") await connection.ExecuteAsync(drop);
            if (phase == "legacy") await connection.ExecuteAsync("""
                ALTER TABLE fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_StatusKey
                CHECK (StatusKey IN ('uploaded','preview_succeeded','preview_failed','queued','executing','execution_succeeded','execution_partial','execution_failed'))
                """);
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "253_ImportExportTaskCancellation.sql", provider == DatabaseProvider.SqlServer);
            Assert.AreEqual(1, (await scope.Runner.MigrateAsync()).ExecutedScriptCount, phase);
            Assert.AreEqual(0, (await scope.Runner.MigrateAsync()).ExecutedScriptCount, phase);
            Assert.AreEqual(1, await connection.ExecuteAsync("UPDATE fn_import_export_task SET StatusKey='cancelled' WHERE Id=@Id", new { Id = id }));
            Assert.AreEqual(7L, await connection.QuerySingleAsync<long>("SELECT Version FROM fn_import_export_task WHERE Id=@Id AND NextLineNumber=1 AND SucceededRowCount=1", new { Id = id }));
            await Assert.ThrowsAsync<DbException>(async () => await connection.ExecuteAsync("UPDATE fn_import_export_task SET StatusKey='invalid' WHERE Id=@Id", new { Id = id }));
            // 最后一次重入须保留前向写入的 cancelled 行，不能只证明旧状态仍能通过。
            if (phase == "legacy")
                await connection.ExecuteAsync("UPDATE fn_import_export_task SET StatusKey='queued' WHERE Id=@Id", new { Id = id });
        }
    }

    // 精确冻结迁移上界，后续迁移不能扩张本次恢复断言。
    private sealed class Through253Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-import-cancel253-").FullName;
        internal DbUpMigrationRunner Runner { get; }
        internal Through253Scope(DatabaseProvider provider, string cs)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames().Where(name =>
                name.Contains(segment, StringComparison.Ordinal) && NamingExpandTestMigrationRunner.IsThroughMigration(name, 253)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("253_ImportExportTaskCancellation.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), JsonSerializer.Serialize(new {
                migrationInventory = new { selectionStatus = "preset-recovery-through253", scripts = names.Select(name =>
                    new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] }) } }));
            Runner = new(Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = cs,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }), NullLoggerFactory.Instance,
                MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
