using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>部分扩展与未记账恢复保留当前活动密钥、准备材料和固定操作事实。</summary>
[TestClass]
public sealed class Migration373WebhookRotationRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Partial_rotation_schema_and_journal_replay_preserve_prepared_key_and_repair_unique_index(DatabaseProvider provider)
    {
        var cs=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using(var before=new ThroughScope(provider,cs,372))await before.Runner.MigrateAsync();
        await using DbConnection db=provider==DatabaseProvider.SqlServer?new SqlConnection(cs):ReviewFixMigrationRecoverySupport.MySqlConnection(cs);await db.OpenAsync();
        var id=Guid.CreateVersion7();var tenant=Guid.CreateVersion7();var rotation=Guid.CreateVersion7();var now=provider==DatabaseProvider.SqlServer?(object)DateTimeOffset.UtcNow:DateTime.UtcNow;
        await db.ExecuteAsync("INSERT INTO fn_webhooks_subscription (Id,TenantId,EventType,TargetUrl,SigningSecretHash,IsActive,CreatedAtUtc,UpdatedAtUtc,Version,SigningKeyId) VALUES (@Id,@Tenant,'event','https://93.184.216.34/key','active-cipher',1,@Now,@Now,2,@Id)",new{Id=id,Tenant=tenant,Now=now});
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"ALTER TABLE fn_webhooks_subscription ADD PreparedRotationId uniqueidentifier NULL":"ALTER TABLE fn_webhooks_subscription ADD COLUMN PreparedRotationId binary(16) NULL");
        using var migration=new ThroughScope(provider,cs,373);Assert.AreEqual(1,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        await db.ExecuteAsync("""
            INSERT INTO fn_webhooks_key_rotation (Id,TenantId,OperationId,ActorUserId,ActorSessionId,SubscriptionId,PreviousKeyId,PreviousVersion,PreparedVersion,StateKey,ProtectedSecret,ProtectedFingerprint,PreparedAtUtc)
            VALUES (@Rotation,@Tenant,@Rotation,@Rotation,@Rotation,@Id,@Id,1,2,'prepared','pending-cipher','protected-fingerprint',@Now)
            """,new{Rotation=rotation,Tenant=tenant,Id=id,Now=now});
        await db.ExecuteAsync("UPDATE fn_webhooks_subscription SET PreparedRotationId=@Rotation WHERE Id=@Id",new{Rotation=rotation,Id=id});
        await db.ExecuteAsync("DROP INDEX UX_fn_webhooks_key_rotation_TenantOperation ON fn_webhooks_key_rotation");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db,"373_WebhookSigningKeyRotations.sql",provider==DatabaseProvider.SqlServer);Assert.AreEqual(1,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db,"373_WebhookSigningKeyRotations.sql",provider==DatabaseProvider.SqlServer);Assert.AreEqual(1,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);Assert.AreEqual(0,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_subscription WHERE Id=@Id AND SigningKeyId=@Id AND SigningSecretHash='active-cipher' AND PreparedRotationId=@Rotation AND Version=2",new{Id=id,Rotation=rotation}));
        Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_key_rotation WHERE Id=@Rotation AND StateKey='prepared' AND ProtectedSecret='pending-cipher' AND ProtectedFingerprint='protected-fingerprint'",new{Rotation=rotation}));
        var unique=provider==DatabaseProvider.SqlServer?"SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_key_rotation') AND name='UX_fn_webhooks_key_rotation_TenantOperation' AND is_unique=1":"SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_key_rotation' AND INDEX_NAME='UX_fn_webhooks_key_rotation_TenantOperation' AND NON_UNIQUE=0";Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>(unique));
        var comment=provider==DatabaseProvider.SqlServer?"SELECT COUNT(*) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_subscription') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'),N'PreparedRotationId','ColumnId') AND name=N'MS_Description'":"SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_subscription' AND COLUMN_NAME='PreparedRotationId' AND COLUMN_COMMENT<>''";Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>(comment));
    }
    private sealed class ThroughScope:IDisposable
    {
        private readonly string directory=Directory.CreateTempSubdirectory("fullnet-webhooks373-").FullName;
        internal DbUpMigrationRunner Runner{get;}
        internal ThroughScope(DatabaseProvider provider,string cs,int maximum)
        {
            var segment=provider==DatabaseProvider.SqlServer?".Migrations.SqlServer.":".Migrations.MySql.";
            var names=typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames().Where(name=>name.Contains(segment,StringComparison.Ordinal)&&NamingExpandTestMigrationRunner.IsThroughMigration(name,maximum)).ToArray();
            File.WriteAllText(Path.Combine(directory,"framework-manifest.json"),System.Text.Json.JsonSerializer.Serialize(new{migrationInventory=new{selectionStatus="preset-rotation-through"+maximum,scripts=names.Select(name=>new{name=name[(name.IndexOf(segment,StringComparison.Ordinal)+segment.Length)..]})}}));
            Runner=new(Options.Create(new DatabaseOptions{Provider=provider,ConnectionString=cs,MySqlGuidStorageMode=MySqlGuidStorageMode.Binary16,CommandTimeoutSeconds=300}),Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,MigrationContractOptionFactory.UuidOptions(),MigrationContractOptionFactory.NamingOptions(),Options.Create(new FrameworkManifestMigrationOptions{ContentRoot=directory}));
        }
        public void Dispose()=>Directory.Delete(directory,recursive:true);
    }
}
