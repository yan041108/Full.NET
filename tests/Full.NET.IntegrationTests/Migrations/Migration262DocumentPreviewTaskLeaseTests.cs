using System.Data.Common;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>按列、索引的未记账阶段恢复扩展，既有任务、文件引用和已写租约身份保持原值。</summary>
[TestClass]
public sealed class Migration262DocumentPreviewTaskLeaseTests
{
    [TestMethod]
    public Task SqlServer_lease_recovers_partial_unjournaled_ddl() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_lease_recovers_partial_unjournaled_ddl() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var scope = new Through262Scope(provider, cs);
        await scope.Runner.MigrateAsync();
        await using DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await connection.OpenAsync();
        var item = Guid.CreateVersion7(); var id = Guid.CreateVersion7(); var file = Guid.CreateVersion7(); var output = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7(); var lease = Guid.CreateVersion7(); var now = DateTime.UtcNow;
        await connection.ExecuteAsync("""
            INSERT INTO fn_document_item (Id,TenantId,Title,CreatedAtUtc,CreatedByUserId,Version)
            VALUES (@Id,NULL,'Lease recovery',@Now,@Actor,1)
            """, new { Id = item, Now = now, Actor = actor });
        await connection.ExecuteAsync("""
            INSERT INTO fn_document_preview_task (Id,DocumentItemId,DocumentTitle,SourceFileId,OutputFileId,StatusKey,ProviderKey,
                RequestedByUserId,CreatedAtUtc,StartedAtUtc,Version)
            VALUES (@Id,@Item,'Lease recovery',@File,@Output,'processing','disabled',@Actor,@Now,@Now,7)
            """, new { Id = id, Item = item, File = file, Output = output, Actor = actor, Now = now });

        foreach (var phase in new[] { "legacy", "lease-id-only", "both-columns", "complete" })
        {
            if (phase != "complete")
                await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
                    ? "DROP INDEX IX_fn_document_preview_task_StatusKey_LeaseExpiresAtUtc ON dbo.fn_document_preview_task"
                    : "ALTER TABLE fn_document_preview_task DROP INDEX IX_fn_document_preview_task_StatusKey_LeaseExpiresAtUtc");
            if (phase is "legacy" or "lease-id-only")
                await connection.ExecuteAsync("ALTER TABLE fn_document_preview_task DROP COLUMN LeaseExpiresAtUtc");
            if (phase == "legacy") await connection.ExecuteAsync("ALTER TABLE fn_document_preview_task DROP COLUMN LeaseId");
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "262_DocumentPreviewTaskLease.sql", provider == DatabaseProvider.SqlServer);
            Assert.AreEqual(1, (await scope.Runner.MigrateAsync()).ExecutedScriptCount, phase);
            Assert.AreEqual(0, (await scope.Runner.MigrateAsync()).ExecutedScriptCount, phase);
            var row = await connection.QuerySingleAsync<Preserved>("""
                SELECT Id,SourceFileId,OutputFileId,StatusKey,Version,LeaseId FROM fn_document_preview_task WHERE Id=@Id
                """, new { Id = id });
            Assert.AreEqual(file, row.SourceFileId); Assert.AreEqual(output, row.OutputFileId);
            Assert.AreEqual("processing", row.StatusKey); Assert.AreEqual(7L, row.Version);
            Assert.AreEqual(phase == "legacy" ? (Guid?)null : lease, row.LeaseId, phase);
            Assert.AreEqual(1, await connection.QuerySingleAsync<int>(provider == DatabaseProvider.SqlServer
                ? "SELECT COUNT(1) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_document_preview_task') AND name='IX_fn_document_preview_task_StatusKey_LeaseExpiresAtUtc'"
                : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_document_preview_task' AND INDEX_NAME='IX_fn_document_preview_task_StatusKey_LeaseExpiresAtUtc'"));
            await connection.ExecuteAsync("UPDATE fn_document_preview_task SET LeaseId=@Lease,LeaseExpiresAtUtc=@Expires WHERE Id=@Id",
                new { Id = id, Lease = lease, Expires = now.AddMinutes(5) });
        }
    }

    private sealed class Preserved
    {
        public Guid Id { get; set; }
        public Guid SourceFileId { get; set; }
        public Guid? OutputFileId { get; set; }
        public string StatusKey { get; set; } = "";
        public long Version { get; set; }
        public Guid? LeaseId { get; set; }
    }

    private sealed class Through262Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-document-lease262-").FullName;
        public DbUpMigrationRunner Runner { get; }
        public Through262Scope(DatabaseProvider provider, string cs)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames().Where(name => name.Contains(segment, StringComparison.Ordinal)
                && NamingExpandTestMigrationRunner.IsThroughMigration(name, 262)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("262_DocumentPreviewTaskLease.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), JsonSerializer.Serialize(new
            {
                migrationInventory = new { selectionStatus = "preset-recovery-through262", scripts = names.Select(name =>
                    new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] }) },
            }));
            Runner = new(Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = cs,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }), NullLoggerFactory.Instance,
                MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, true);
    }
}
