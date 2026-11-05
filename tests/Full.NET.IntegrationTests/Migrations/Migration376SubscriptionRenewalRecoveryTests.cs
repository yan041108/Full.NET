using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>续期来源逐列恢复、重复记账与成对约束在双库保持一致。</summary>
[TestClass]
public sealed class Migration376SubscriptionRenewalRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Partial_source_columns_and_unjournaled_replay_converge(DatabaseProvider provider)
    {
        var cs=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using(var before=new ThroughScope(provider,cs,375)) await before.Runner.MigrateAsync();
        await using DbConnection db=provider==DatabaseProvider.SqlServer?new SqlConnection(cs):ReviewFixMigrationRecoverySupport.MySqlConnection(cs); await db.OpenAsync();
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"ALTER TABLE fn_tenancy_subscription_invoice ADD RenewalOfSubscriptionId uniqueidentifier NULL":"ALTER TABLE fn_tenancy_subscription_invoice ADD COLUMN RenewalOfSubscriptionId binary(16) NULL COMMENT '本次独立账单的原续期订阅'");
        using var migration=new ThroughScope(provider,cs,376); Assert.AreEqual(1,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        for(var replay=0;replay<2;replay++)
        {
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db,"376_SubscriptionRenewalSource.sql",provider==DatabaseProvider.SqlServer);
            Assert.AreEqual(1,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        }
        Assert.AreEqual(0,(await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        var columns=provider==DatabaseProvider.SqlServer?"SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND name IN ('RenewalOfSubscriptionId','RenewalSourceVersion') AND is_nullable=1":"SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_tenancy_subscription_invoice' AND COLUMN_NAME IN ('RenewalOfSubscriptionId','RenewalSourceVersion') AND IS_NULLABLE='YES'";
        Assert.AreEqual(2L,await db.ExecuteScalarAsync<long>(columns));
        var check=provider==DatabaseProvider.SqlServer?"SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND name='ck_fn_tenancy_subscription_invoice_renewal_source' AND is_disabled=0 AND is_not_trusted=0":"SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_tenancy_subscription_invoice' AND CONSTRAINT_NAME='ck_fn_tenancy_subscription_invoice_renewal_source' AND ENFORCED='YES'";
        Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>(check));
    }
    private sealed class ThroughScope:IDisposable
    {
        private readonly string directory=Directory.CreateTempSubdirectory("fullnet-renewal376-").FullName;
        internal DbUpMigrationRunner Runner{get;}
        internal ThroughScope(DatabaseProvider provider,string cs,int maximum)
        {
            var segment=provider==DatabaseProvider.SqlServer?".Migrations.SqlServer.":".Migrations.MySql.";
            var names=typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames().Where(name=>name.Contains(segment,StringComparison.Ordinal)&&NamingExpandTestMigrationRunner.IsThroughMigration(name,maximum)).ToArray();
            File.WriteAllText(Path.Combine(directory,"framework-manifest.json"),System.Text.Json.JsonSerializer.Serialize(new{migrationInventory=new{selectionStatus="preset-renewal-through"+maximum,scripts=names.Select(name=>new{name=name[(name.IndexOf(segment,StringComparison.Ordinal)+segment.Length)..]})}}));
            Runner=new(Options.Create(new DatabaseOptions{Provider=provider,ConnectionString=cs,MySqlGuidStorageMode=MySqlGuidStorageMode.Binary16,CommandTimeoutSeconds=300}),Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,MigrationContractOptionFactory.UuidOptions(),MigrationContractOptionFactory.NamingOptions(),Options.Create(new FrameworkManifestMigrationOptions{ContentRoot=directory}));
        }
        public void Dispose()=>Directory.Delete(directory,recursive:true);
    }
}
