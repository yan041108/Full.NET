using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>历史索引缺失和已完成未记账两种恢复均不得改动投递原文、状态与修订。</summary>
[TestClass]
public sealed class Migration369WebhookDeliveryHistoryRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task History_index_replay_preserves_delivery_facts(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var migration = new Through369Scope(provider, cs); await migration.Runner.MigrateAsync();
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs); await db.OpenAsync();
        var subscription = Guid.CreateVersion7(); var id = Guid.CreateVersion7(); var now = provider == DatabaseProvider.SqlServer ? (object)DateTimeOffset.UtcNow : DateTime.UtcNow;
        await db.ExecuteAsync("""
            INSERT INTO fn_webhooks_subscription (Id,TenantId,EventType,TargetUrl,SigningSecretHash,IsActive,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Subscription,@Tenant,'history.test','https://example.com','protected-fixture',1,@Now,@Now,1)
            """, new { Subscription = subscription, Tenant = Guid.CreateVersion7(), Now = now });
        await db.ExecuteAsync("""
            INSERT INTO fn_webhooks_delivery (Id,SubscriptionId,EventId,PayloadBody,PayloadDigest,Status,AttemptCount,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@Subscription,@Event,'original-payload','original-digest','Failed',3,@Now,@Now,7)
            """, new { Id = id, Subscription = subscription, Event = Guid.CreateVersion7(), Now = now });
        await db.ExecuteAsync("DROP INDEX IX_fn_webhooks_delivery_SubscriptionCreatedId ON fn_webhooks_delivery");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "369_WebhookDeliveryHistoryIndex.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "369_WebhookDeliveryHistoryIndex.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount); Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(1L, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery WHERE Id=@Id AND PayloadBody='original-payload' AND PayloadDigest='original-digest' AND Status='Failed' AND AttemptCount=3 AND Version=7", new { Id = id }));
        var columns = provider == DatabaseProvider.SqlServer ? "SELECT col.name FROM sys.indexes idx JOIN sys.index_columns ic ON ic.object_id=idx.object_id AND ic.index_id=idx.index_id JOIN sys.columns col ON col.object_id=ic.object_id AND col.column_id=ic.column_id WHERE idx.object_id=OBJECT_ID(N'dbo.fn_webhooks_delivery') AND idx.name='IX_fn_webhooks_delivery_SubscriptionCreatedId' ORDER BY ic.key_ordinal"
            : "SELECT COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_delivery' AND INDEX_NAME='IX_fn_webhooks_delivery_SubscriptionCreatedId' ORDER BY SEQ_IN_INDEX";
        CollectionAssert.AreEqual(new[] { "SubscriptionId", "CreatedAtUtc", "Id" }, (await db.QueryAsync<string>(columns)).ToArray());
    }
    private sealed class Through369Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-webhooks369-").FullName;
        internal DbUpMigrationRunner Runner { get; }
        internal Through369Scope(DatabaseProvider provider, string cs)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames().Where(name => name.Contains(segment, StringComparison.Ordinal) && NamingExpandTestMigrationRunner.IsThroughMigration(name, 369)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("369_WebhookDeliveryHistoryIndex.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new { migrationInventory = new { selectionStatus = "preset-webhook-history-through369", scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] }) } }));
            Runner = new(Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = cs, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(), Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
