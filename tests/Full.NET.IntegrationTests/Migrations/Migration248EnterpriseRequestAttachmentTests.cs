using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>附件引用双库真实 UUID、唯一文件归属及未记账重入验证。</summary>
[TestClass]
public sealed class Migration248EnterpriseRequestAttachmentTests
{
    [TestMethod]
    public Task SqlServer_attachment_reference_reentry_preserves_data() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_attachment_reference_reentry_preserves_data() => VerifyAsync(DatabaseProvider.MySql);
    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var migration = new Through248Scope(provider, cs); await migration.Runner.MigrateAsync();
        await using DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await connection.OpenAsync();
        var values = new { Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), RequestId = Guid.CreateVersion7(),
            FileId = Guid.CreateVersion7(), ActorId = Guid.CreateVersion7(), Now = DateTime.UtcNow };
        const string insert = """
            INSERT INTO demo_enterprise_request_request_attachment
              (Id, TenantId, RequestId, FileId, OriginalFileName, SizeBytes, CreatedAtUtc, CreatedById, StateKey, UploadExpiresAtUtc)
            VALUES (@Id, @TenantId, @RequestId, @FileId, 'probe.txt', 12, @Now, @ActorId, 'bound', @Now)
            """;
        await connection.ExecuteAsync(insert, values);
        if (provider == DatabaseProvider.SqlServer)
            await connection.ExecuteAsync("DROP INDEX UX_demo_enterprise_request_request_attachment_FileId ON dbo.demo_enterprise_request_request_attachment");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "248_DemoEnterpriseRequestAttachment.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(values.FileId, await connection.ExecuteScalarAsync<Guid>("SELECT FileId FROM demo_enterprise_request_request_attachment WHERE TenantId = @TenantId AND Id = @Id", values));
        await Assert.ThrowsAsync<DbException>(async () => await connection.ExecuteAsync(insert, new {
            Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), RequestId = Guid.CreateVersion7(), values.FileId, values.ActorId, values.Now }));
    }
    // 目标脚本上界固定，新增迁移不改变本用例的恢复语义。
    private sealed class Through248Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-attachment248-").FullName;
        internal Full.NET.Migrations.DbUp.DbUpMigrationRunner Runner { get; }
        internal Through248Scope(DatabaseProvider provider, string cs)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(Full.NET.Migrations.DbUp.DbUpMigrationRunner).Assembly.GetManifestResourceNames()
                .Where(name => name.Contains(segment, StringComparison.Ordinal) && NamingExpandTestMigrationRunner.IsThroughMigration(name, 248)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("248_DemoEnterpriseRequestAttachment.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new {
                migrationInventory = new { selectionStatus = "preset-recovery-through248", scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] }) } }));
            Runner = new(Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { Provider = provider,
                    ConnectionString = cs, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Microsoft.Extensions.Options.Options.Create(new Full.NET.Migrations.DbUp.FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
