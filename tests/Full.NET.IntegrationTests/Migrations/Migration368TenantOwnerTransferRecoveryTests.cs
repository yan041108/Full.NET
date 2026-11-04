using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>所有者回执的部分索引/元数据恢复不得改写已提交事实；冻结迁移上界避免后续脚本漂移。</summary>
[TestClass]
public sealed class Migration368TenantOwnerTransferRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Missing_indexes_and_unjournaled_replay_preserve_receipts(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var migration = new Through368Scope(provider, cs); await migration.Runner.MigrateAsync();
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await db.OpenAsync();
        const string insert = """
            INSERT INTO fn_identity_tenant_owner_transfer
                (Id,TenantId,OperationId,ActorUserId,TargetMemberId,TargetUserId,TargetPreviousVersion,TargetVersion,CreatedAtUtc,RequestDigest)
            VALUES (@Id,@Tenant,@Operation,@Actor,@Member,@Actor,2,3,@Now,@Digest)
            """;
        var id = Guid.CreateVersion7(); var tenant = Guid.CreateVersion7(); var operation = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        object now = provider == DatabaseProvider.SqlServer ? DateTimeOffset.UtcNow : DateTime.UtcNow;
        var arguments = new { Id = id, Tenant = tenant, Operation = operation, Actor = actor, Member = Guid.CreateVersion7(), Now = now, Digest = new string('A', 64) };
        await db.ExecuteAsync(insert, arguments);
        // 只操作本夹具新建的数据库及指定索引；保留表和回执，模拟尚未记账的缺失步骤。
        await db.ExecuteAsync("DROP INDEX UX_fn_identity_tenant_owner_transfer_TenantOperation ON fn_identity_tenant_owner_transfer");
        await db.ExecuteAsync("DROP INDEX IX_fn_identity_tenant_member_TenantId_MemberRole_Id ON fn_identity_tenant_member");
        await db.ExecuteAsync("DROP INDEX IX_fn_identity_tenant_member_UserId_MemberRole ON fn_identity_tenant_member");
        if (provider == DatabaseProvider.SqlServer) await db.ExecuteAsync("""
            EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo',
                 @level1type=N'TABLE', @level1name=N'fn_identity_tenant_owner_transfer', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc'
            """);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "368_TenantOwnerTransferReceipt.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        var saved = await db.QuerySingleAsync<ReceiptRow>("SELECT Id,ActorUserId,TargetVersion,RequestDigest FROM fn_identity_tenant_owner_transfer WHERE TenantId=@Tenant AND OperationId=@Operation", arguments);
        Assert.AreEqual(id, saved.Id); Assert.AreEqual(actor, saved.ActorUserId); Assert.AreEqual(3, saved.TargetVersion); Assert.AreEqual(arguments.Digest, saved.RequestDigest);
        var indexes = provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE name IN ('UX_fn_identity_tenant_owner_transfer_TenantOperation','IX_fn_identity_tenant_member_TenantId_MemberRole_Id','IX_fn_identity_tenant_member_UserId_MemberRole')"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND INDEX_NAME IN ('UX_fn_identity_tenant_owner_transfer_TenantOperation','IX_fn_identity_tenant_member_TenantId_MemberRole_Id','IX_fn_identity_tenant_member_UserId_MemberRole')";
        Assert.AreEqual(3L, await db.ExecuteScalarAsync<long>(indexes));
        if (provider == DatabaseProvider.SqlServer) Assert.AreEqual(1L, await db.ExecuteScalarAsync<long>("""
            SELECT COUNT(*) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer')
              AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_owner_transfer'),'CreatedAtUtc','ColumnId') AND name='MS_Description'
            """));
        var duplicate = await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync(insert, arguments with { Id = Guid.CreateVersion7() }));
        StringAssert.Contains(duplicate.Message, "UX_fn_identity_tenant_owner_transfer_TenantOperation");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "368_TenantOwnerTransferReceipt.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(1L, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_identity_tenant_owner_transfer WHERE TenantId=@Tenant AND OperationId=@Operation", arguments));
    }

    private sealed class ReceiptRow { public Guid Id { get; set; } public Guid ActorUserId { get; set; } public int TargetVersion { get; set; } public string RequestDigest { get; set; } = ""; }
    private sealed class Through368Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-owner368-").FullName;
        internal DbUpMigrationRunner Runner { get; }
        internal Through368Scope(DatabaseProvider provider, string cs)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
                .Where(name => name.Contains(segment, StringComparison.Ordinal) && NamingExpandTestMigrationRunner.IsThroughMigration(name, 368)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("368_TenantOwnerTransferReceipt.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new {
                migrationInventory = new { selectionStatus = "preset-owner-recovery-through368", scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] }) } }));
            Runner = new(Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = cs,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
