using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>重放回执的索引/说明恢复及未记账重跑，不能改变原操作或审计事实。</summary>
[TestClass]
public sealed class Migration371WebhookReplayRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Replay_receipt_recovery_preserves_original_audit_and_unique_operation(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var migration = new Through371Scope(provider, cs); await migration.Runner.MigrateAsync();
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs); await db.OpenAsync();
        var id = Guid.CreateVersion7(); var tenant = Guid.CreateVersion7(); var operation = Guid.CreateVersion7();
        await db.ExecuteAsync("""
            INSERT INTO fn_webhooks_delivery_replay (Id,TenantId,OperationId,ActorUserId,ActorSessionId,SubscriptionId,DeliveryId,EventId,PreviousVersion,Version,AttemptCount,QueuedAtUtc)
            VALUES (@Id,@Tenant,@Operation,@Actor,@Session,@Subscription,@Delivery,@Event,3,4,5,@Now)
            """, new { Id = id, Tenant = tenant, Operation = operation, Actor = Guid.CreateVersion7(), Session = Guid.CreateVersion7(), Subscription = Guid.CreateVersion7(), Delivery = Guid.CreateVersion7(), Event = Guid.CreateVersion7(), Now = provider == DatabaseProvider.SqlServer ? (object)DateTimeOffset.UtcNow : DateTime.UtcNow });
        await db.ExecuteAsync("DROP INDEX UX_fn_webhooks_delivery_replay_TenantOperation ON fn_webhooks_delivery_replay");
        if (provider == DatabaseProvider.SqlServer)
        {
            await db.ExecuteAsync("DROP INDEX CX_fn_webhooks_delivery_replay_QueuedId ON fn_webhooks_delivery_replay");
            await db.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_replay',@level2type=N'COLUMN',@level2name=N'ActorSessionId'");
        }
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "371_WebhookReplayReceipts.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "371_WebhookReplayReceipts.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount); Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(1L, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery_replay WHERE Id=@Id AND TenantId=@Tenant AND OperationId=@Operation AND PreviousVersion=3 AND Version=4 AND AttemptCount=5 AND ActorUserId IS NOT NULL AND ActorSessionId IS NOT NULL", new { Id = id, Tenant = tenant, Operation = operation }));
        var sql = provider == DatabaseProvider.SqlServer ? "SELECT col.name FROM sys.indexes idx JOIN sys.index_columns ic ON ic.object_id=idx.object_id AND ic.index_id=idx.index_id JOIN sys.columns col ON col.object_id=ic.object_id AND col.column_id=ic.column_id WHERE idx.object_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND idx.name='UX_fn_webhooks_delivery_replay_TenantOperation' ORDER BY ic.key_ordinal"
            : "SELECT COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_delivery_replay' AND INDEX_NAME='UX_fn_webhooks_delivery_replay_TenantOperation' ORDER BY SEQ_IN_INDEX";
        CollectionAssert.AreEqual(new[] { "TenantId", "OperationId" }, (await db.QueryAsync<string>(sql)).ToArray());
        if (provider == DatabaseProvider.SqlServer) Assert.AreEqual(13L, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_replay') AND name=N'MS_Description'"));
    }
    private sealed class Through371Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-webhooks371-").FullName;
        internal DbUpMigrationRunner Runner { get; }
        internal Through371Scope(DatabaseProvider provider, string cs)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames().Where(name => name.Contains(segment, StringComparison.Ordinal) && NamingExpandTestMigrationRunner.IsThroughMigration(name, 371)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("371_WebhookReplayReceipts.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new { migrationInventory = new { selectionStatus = "preset-webhook-replay-through371", scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] }) } }));
            Runner = new(Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = cs, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(), Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
