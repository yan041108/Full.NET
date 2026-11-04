using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>签名身份部分回填与未记账恢复不能重写已有身份、秘密、次数或版本。</summary>
[TestClass]
public sealed class Migration372WebhookSigningKeyRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Key_identity_recovery_backfills_only_missing_values_and_restores_index_and_comment(DatabaseProvider provider)
    {
        var cs=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using(var before=new ThroughScope(provider,cs,371))await before.Runner.MigrateAsync();
        await using DbConnection db=provider==DatabaseProvider.SqlServer?new SqlConnection(cs):ReviewFixMigrationRecoverySupport.MySqlConnection(cs);await db.OpenAsync();
        var first=Guid.CreateVersion7();var second=Guid.CreateVersion7();var retained=Guid.CreateVersion7();
        foreach(var id in new[]{first,second})await db.ExecuteAsync("""
            INSERT INTO fn_webhooks_subscription (Id,TenantId,EventType,TargetUrl,SigningSecretHash,IsActive,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@Tenant,'fullnet.workflow.instance.completed','https://93.184.216.34/key','unchanged-cipher',1,@Now,@Now,7)
            """,new{Id=id,Tenant=Guid.CreateVersion7(),Now=provider==DatabaseProvider.SqlServer?(object)DateTimeOffset.UtcNow:DateTime.UtcNow});
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"ALTER TABLE fn_webhooks_subscription ADD SigningKeyId uniqueidentifier NULL":"ALTER TABLE fn_webhooks_subscription ADD COLUMN SigningKeyId binary(16) NULL");
        await db.ExecuteAsync("UPDATE fn_webhooks_subscription SET SigningKeyId=@Key WHERE Id=@Id",new{Key=retained,Id=first});
        using var migration=new ThroughScope(provider,cs,372);Assert.AreEqual(1,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        await db.ExecuteAsync("DROP INDEX UX_fn_webhooks_subscription_SigningKeyId ON fn_webhooks_subscription");
        if(provider==DatabaseProvider.SqlServer)await db.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_subscription',@level2type=N'COLUMN',@level2name=N'SigningKeyId'");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db,"372_WebhookSigningKeyIdentity.sql",provider==DatabaseProvider.SqlServer);Assert.AreEqual(1,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db,"372_WebhookSigningKeyIdentity.sql",provider==DatabaseProvider.SqlServer);Assert.AreEqual(1,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);Assert.AreEqual(0,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(retained,await db.ExecuteScalarAsync<Guid>("SELECT SigningKeyId FROM fn_webhooks_subscription WHERE Id=@Id",new{Id=first}));Assert.AreEqual(second,await db.ExecuteScalarAsync<Guid>("SELECT SigningKeyId FROM fn_webhooks_subscription WHERE Id=@Id",new{Id=second}));
        Assert.AreEqual(2L,await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_subscription WHERE SigningSecretHash='unchanged-cipher' AND Version=7"));
        var nonnull=provider==DatabaseProvider.SqlServer?"SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_subscription') AND name='SigningKeyId' AND is_nullable=0":"SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_subscription' AND COLUMN_NAME='SigningKeyId' AND IS_NULLABLE='NO'";Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>(nonnull));
        var unique=provider==DatabaseProvider.SqlServer?"SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_subscription') AND name='UX_fn_webhooks_subscription_SigningKeyId' AND is_unique=1":"SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_subscription' AND INDEX_NAME='UX_fn_webhooks_subscription_SigningKeyId' AND NON_UNIQUE=0";Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>(unique));
    }
    private sealed class ThroughScope:IDisposable
    {
        private readonly string directory=Directory.CreateTempSubdirectory("fullnet-webhooks372-").FullName;
        internal DbUpMigrationRunner Runner{get;}
        internal ThroughScope(DatabaseProvider provider,string cs,int maximum)
        {
            var segment=provider==DatabaseProvider.SqlServer?".Migrations.SqlServer.":".Migrations.MySql.";
            var names=typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames().Where(name=>name.Contains(segment,StringComparison.Ordinal)&&NamingExpandTestMigrationRunner.IsThroughMigration(name,maximum)).ToArray();Assert.IsTrue(names.Any(name=>name.EndsWith(maximum==372?"372_WebhookSigningKeyIdentity.sql":"371_WebhookReplayReceipts.sql",StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory,"framework-manifest.json"),System.Text.Json.JsonSerializer.Serialize(new{migrationInventory=new{selectionStatus="preset-signature-key-through"+maximum,scripts=names.Select(name=>new{name=name[(name.IndexOf(segment,StringComparison.Ordinal)+segment.Length)..]})}}));
            Runner=new(Options.Create(new DatabaseOptions{Provider=provider,ConnectionString=cs,MySqlGuidStorageMode=MySqlGuidStorageMode.Binary16,CommandTimeoutSeconds=300}),Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,MigrationContractOptionFactory.UuidOptions(),MigrationContractOptionFactory.NamingOptions(),Options.Create(new FrameworkManifestMigrationOptions{ContentRoot=directory}));
        }
        public void Dispose()=>Directory.Delete(directory,recursive:true);
    }
}
