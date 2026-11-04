using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>尝试索引/说明缺失及未记账重跑保留已完成证据，不补造旧投递尝试。</summary>
[TestClass]
public sealed class Migration370WebhookAttemptsRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Attempt_schema_replay_preserves_completed_results(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var migration = new Through370Scope(provider, cs); await migration.Runner.MigrateAsync();
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs); await db.OpenAsync();
        var id = Guid.CreateVersion7(); var delivery = Guid.CreateVersion7(); var now = provider == DatabaseProvider.SqlServer ? (object)DateTimeOffset.UtcNow : DateTime.UtcNow;
        await db.ExecuteAsync("""
            INSERT INTO fn_webhooks_delivery_attempt (Id,TenantId,SubscriptionId,DeliveryId,ClaimVersion,StartedAtUtc,CompletedAtUtc,OutcomeKey,HttpStatus,ErrorCode,AppliedToDelivery)
            VALUES (@Id,@Tenant,@Subscription,@Delivery,3,@Now,@Now,'failed',503,'webhooks.delivery.http_error',0)
            """, new { Id = id, Tenant = Guid.CreateVersion7(), Subscription = Guid.CreateVersion7(), Delivery = delivery, Now = now });
        await db.ExecuteAsync("DROP INDEX UX_fn_webhooks_delivery_attempt_DeliveryClaim ON fn_webhooks_delivery_attempt");
        if (provider == DatabaseProvider.SqlServer)
        {
            await db.ExecuteAsync("DROP INDEX CX_fn_webhooks_delivery_attempt_StartedId ON fn_webhooks_delivery_attempt");
            await db.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_delivery_attempt',@level2type=N'COLUMN',@level2name=N'CompletedAtUtc'");
        }
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "370_WebhookDeliveryAttempts.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "370_WebhookDeliveryAttempts.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount); Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(1L, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery_attempt WHERE Id=@Id AND DeliveryId=@Delivery AND ClaimVersion=3 AND OutcomeKey='failed' AND HttpStatus=503 AND ErrorCode='webhooks.delivery.http_error' AND AppliedToDelivery=0 AND CompletedAtUtc IS NOT NULL", new { Id = id, Delivery = delivery }));
        var columns = provider == DatabaseProvider.SqlServer ? "SELECT col.name FROM sys.indexes idx JOIN sys.index_columns ic ON ic.object_id=idx.object_id AND ic.index_id=idx.index_id JOIN sys.columns col ON col.object_id=ic.object_id AND col.column_id=ic.column_id WHERE idx.object_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND idx.name='UX_fn_webhooks_delivery_attempt_DeliveryClaim' ORDER BY ic.key_ordinal"
            : "SELECT COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_delivery_attempt' AND INDEX_NAME='UX_fn_webhooks_delivery_attempt_DeliveryClaim' ORDER BY SEQ_IN_INDEX";
        CollectionAssert.AreEqual(new[] { "DeliveryId", "ClaimVersion" }, (await db.QueryAsync<string>(columns)).ToArray());
        if (provider == DatabaseProvider.SqlServer) Assert.AreEqual(12L, await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_delivery_attempt') AND name=N'MS_Description'"));
    }
    private sealed class Through370Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-webhooks370-").FullName;
        internal DbUpMigrationRunner Runner { get; }
        internal Through370Scope(DatabaseProvider provider, string cs)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames().Where(name => name.Contains(segment, StringComparison.Ordinal) && NamingExpandTestMigrationRunner.IsThroughMigration(name, 370)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("370_WebhookDeliveryAttempts.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new { migrationInventory = new { selectionStatus = "preset-webhook-attempts-through370", scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] }) } }));
            Runner = new(Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = cs, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(), Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
