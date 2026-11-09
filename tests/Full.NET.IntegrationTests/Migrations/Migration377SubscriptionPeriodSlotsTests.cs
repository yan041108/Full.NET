using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>已有当前周期的部署升级后可另存待付款周期，并保留两个槽的唯一保护。</summary>
[TestClass]
public sealed class Migration377SubscriptionPeriodSlotsTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Existing_and_unjournaled_slot_indexes_converge(DatabaseProvider provider)
    {
        var sqlServer = provider == DatabaseProvider.SqlServer;
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using (var before = new Migration376SubscriptionRenewalRecoveryTests.ThroughScope(provider, connectionString, 376))
            await before.Runner.MigrateAsync();
        await using DbConnection database = sqlServer ? new SqlConnection(connectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await database.OpenAsync();
        var tenant = Guid.CreateVersion7();
        var original = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await InsertAsync(original, "Active");
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(Guid.CreateVersion7(), "PastDue"));
        if (sqlServer)
            await database.ExecuteAsync("""
                CREATE UNIQUE NONCLUSTERED INDEX UX_fn_tenancy_tenant_subscription_TenantCurrent
                ON dbo.fn_tenancy_tenant_subscription (TenantId) WHERE Status IN (N'Trial', N'Active')
                """);
        else
            await database.ExecuteAsync("ALTER TABLE fn_tenancy_tenant_subscription DROP INDEX UX_fn_tenancy_tenant_subscription_TenantActive");

        using var migration = new Migration376SubscriptionRenewalRecoveryTests.ThroughScope(provider, connectionString, 377);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        var pending = Guid.CreateVersion7();
        await InsertAsync(pending, "PastDue");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(database, "377_SubscriptionPeriodSlots.sql", sqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(Guid.CreateVersion7(), "Trial"));
        await Assert.ThrowsAsync<DbException>(() => InsertAsync(Guid.CreateVersion7(), "PastDue"));
        Assert.AreEqual(2, await database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_tenancy_tenant_subscription WHERE TenantId=@Tenant", new { Tenant = tenant }));
        Assert.AreEqual("Active", await ReadStatusAsync(original));
        Assert.AreEqual("PastDue", await ReadStatusAsync(pending));

        Task InsertAsync(Guid id, string status) => database.ExecuteAsync("""
            INSERT INTO fn_tenancy_tenant_subscription
                (Id,TenantId,Status,CurrentPeriodStartUtc,CurrentPeriodEndUtc,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@Tenant,@Status,@Start,@End,@Now,@Now,1)
            """, new { Id = id, Tenant = tenant, Status = status, Start = now, End = now.AddDays(30), Now = now });

        Task<string> ReadStatusAsync(Guid id) => database.QuerySingleAsync<string>(
            "SELECT Status FROM fn_tenancy_tenant_subscription WHERE Id=@Id", new { Id = id });
    }
}
