using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>分类/标签门禁的双库部分执行恢复、Host 约束及历史数据保留验证。</summary>
[TestClass]
public sealed class Migration251DocumentTaxonomyWriteGuardTests
{
    private static readonly Guid GuardId = Guid.Parse("01a12ef8-d36c-78d9-b24b-93aa39be0747");

    [TestMethod]
    public Task SqlServer_write_guard_recovers_unjournaled_phases() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_write_guard_recovers_unjournaled_phases() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var migration = new Through251Scope(provider, cs);
        await migration.Runner.MigrateAsync();
        await using DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await connection.OpenAsync();
        var sentinel = new { Id = Guid.CreateVersion7(), Title = "guard-migration-sentinel", Now = DateTime.UtcNow, Actor = Guid.CreateVersion7() };
        await connection.ExecuteAsync("""
            INSERT INTO fn_document_item (Id, TenantId, Title, CreatedAtUtc, CreatedByUserId, Version)
            VALUES (@Id, NULL, @Title, @Now, @Actor, 1)
            """, sentinel);

        // 模拟表已创建、后续约束和技术行未完成且 DbUp 尚未记账的恢复窗口。
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE dbo.fn_document_taxonomy_write_guard DROP CONSTRAINT CK_fn_document_taxonomy_write_guard_HostScope"
            : "ALTER TABLE fn_document_taxonomy_write_guard DROP CHECK CK_fn_document_taxonomy_write_guard_HostScope");
        await connection.ExecuteAsync("DELETE FROM fn_document_taxonomy_write_guard WHERE Id = @Id", new { Id = GuardId });
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "251_DocumentTaxonomyWriteGuard.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(1L, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_document_taxonomy_write_guard WHERE Id = @Id AND TenantId IS NULL", new { Id = GuardId }));
        Assert.AreEqual(sentinel.Title, await connection.ExecuteScalarAsync<string>("SELECT Title FROM fn_document_item WHERE Id = @Id AND Version = 1", sentinel));
        await Assert.ThrowsAsync<DbException>(async () => await connection.ExecuteAsync(
            "INSERT INTO fn_document_taxonomy_write_guard (Id, TenantId) VALUES (@Id, @TenantId)",
            new { Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7() }));

        // 模拟另一中断阶段：结构已完整但两行都未插入；每个目录的技术行都必须恢复。
        await connection.ExecuteAsync("DELETE FROM fn_document_taxonomy_write_guard WHERE TenantId IS NULL");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "251_DocumentTaxonomyWriteGuard.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(1L, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_document_taxonomy_write_guard WHERE Id = @Id AND TenantId IS NULL",
            new { Id = Guid.Parse("01a12ef8-d360-7d89-91f6-7e58ddbfcd73") }));

        // 完整结构未记账也可重入，且不会增加重复门禁或改动业务数据。
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "251_DocumentTaxonomyWriteGuard.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(2L, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_document_taxonomy_write_guard"));
    }

    // 冻结目标迁移上界，后续迁移不能改变本用例的恢复语义。
    private sealed class Through251Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-document-guard251-").FullName;
        internal Full.NET.Migrations.DbUp.DbUpMigrationRunner Runner { get; }
        internal Through251Scope(DatabaseProvider provider, string cs)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(Full.NET.Migrations.DbUp.DbUpMigrationRunner).Assembly.GetManifestResourceNames()
                .Where(name => name.Contains(segment, StringComparison.Ordinal) && NamingExpandTestMigrationRunner.IsThroughMigration(name, 251)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("251_DocumentTaxonomyWriteGuard.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new {
                migrationInventory = new { selectionStatus = "preset-recovery-through251", scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] }) } }));
            Runner = new(Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { Provider = provider,
                    ConnectionString = cs, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Microsoft.Extensions.Options.Options.Create(new Full.NET.Migrations.DbUp.FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
