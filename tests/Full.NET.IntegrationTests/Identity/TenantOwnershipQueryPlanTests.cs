using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Features.ManageTenantOwnership;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>验证两个有界职责查询的双库真实计划；小规模单连接证据不推导吞吐或容量。</summary>
[TestClass]
public sealed class TenantOwnershipQueryPlanTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Owner_queries_return_bounded_facts_with_real_plans(DatabaseProvider provider)
    {
        using var f = await TenantOwnershipHandoffTests.Fixture.CreateAsync(provider);
        var owner = await f.AddPersonAsync("plan-owner", "Owner");
        var options = f.Factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(options.ConnectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(options.ConnectionString);
        await db.OpenAsync();
        // 40 家企业各 50 条成员事实，每家一名 Owner；不伪造真实账号或对外发起业务操作。
        var tenants = Enumerable.Range(0, 40).Select(_ => Guid.CreateVersion7()).ToArray();
        object now = provider == DatabaseProvider.SqlServer ? DateTimeOffset.UtcNow : DateTime.UtcNow;
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_tenant_member (Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@TenantId,@UserId,@MemberRole,'Active',@Now,@Now,1)
            """, tenants.SelectMany(tenant => Enumerable.Range(0, 50).Select(index => new {
                Id = Guid.CreateVersion7(), TenantId = tenant, UserId = Guid.CreateVersion7(),
                MemberRole = index == 0 ? "Owner" : "Member", Now = now })));
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "UPDATE STATISTICS fn_identity_tenant_member" : "ANALYZE TABLE fn_identity_tenant_member");
        var parameters = new { TenantId = f.Context.Id, owner.UserId };
        var owners = provider == DatabaseProvider.SqlServer ? TenantOwnershipSql.OwnersSqlServer : TenantOwnershipSql.OwnersMySql;
        Assert.AreEqual(1, (await db.QueryAsync(owners.Text, parameters)).Count());
        Assert.AreEqual(1, await db.QuerySingleAsync<int>(TenantOwnershipSql.HasOwnerResponsibility.Text, parameters));
        var root = Environment.GetEnvironmentVariable("FULLNET_TENANT_OWNER_QUERY_EVIDENCE");
        foreach (var statement in new[] { owners, TenantOwnershipSql.HasOwnerResponsibility })
        {
            string plan;
            if (provider == DatabaseProvider.MySql)
                plan = await db.QuerySingleAsync<string>("EXPLAIN FORMAT=JSON " + statement.Text, parameters)
                    + Environment.NewLine + await db.QuerySingleAsync<string>("EXPLAIN ANALYZE " + statement.Text, parameters);
            else
            {
                await db.ExecuteAsync("SET STATISTICS XML ON"); plan = "";
                try
                {
                    await using var reader = await db.ExecuteReaderAsync(statement.Text, parameters);
                    do
                    {
                        while (await reader.ReadAsync())
                            if (reader.FieldCount == 1 && reader.GetValue(0) is string value && value.Contains("ShowPlanXML", StringComparison.Ordinal)) plan = value;
                    } while (await reader.NextResultAsync());
                }
                finally { await db.ExecuteAsync("SET STATISTICS XML OFF"); }
            }
            Assert.IsFalse(string.IsNullOrWhiteSpace(plan));
            if (!string.IsNullOrWhiteSpace(root))
            {
                var directory = Directory.CreateDirectory(Path.Combine(root, provider.ToString())).FullName;
                await File.WriteAllTextAsync(Path.Combine(directory, statement.Name + ".plan.txt"), plan);
                await File.WriteAllTextAsync(Path.Combine(directory, "environment.txt"),
                    $"Server={db.ServerVersion}; Runtime={Environment.Version}; OS={Environment.OSVersion}; CPU={Environment.ProcessorCount}; seededMembers=2000; seededTenants=40; concurrency=1");
            }
        }
    }
}
