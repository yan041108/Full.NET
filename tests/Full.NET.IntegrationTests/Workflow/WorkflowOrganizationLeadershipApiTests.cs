using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>明确领导权的真实双库配置、并发、隔离、空来源与部分迁移恢复，不允许从普通成员推断。</summary>
[TestClass]
public sealed class WorkflowOrganizationLeadershipApiTests
{
    [TestMethod]
    public async Task Explicit_leadership_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Explicit_leadership_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        using var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = (await loginResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var tenant = (await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(value => value.Identifier == "acme");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id));
        var actor = (await client.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        using var created = await client.PostAsJsonAsync("/api/v1/organization/units", new { parentId = (Guid?)null, code = "leader-" + Guid.NewGuid().ToString("N"), name = "显式负责人", displayOrder = 1 });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var unit = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = unit.GetProperty("id").GetGuid(); var version = unit.GetProperty("version").GetInt32();
        var path = $"/api/v1/organization/units/{id:D}/leadership";
        using var membership = await client.PostAsJsonAsync("/api/v1/organization/user-units", new { userId = actor, unitId = id, isPrimary = true });
        Assert.AreEqual(HttpStatusCode.Created, membership.StatusCode, await membership.Content.ReadAsStringAsync());
        var empty = await client.GetFromJsonAsync<OrganizationUnitLeadershipResponse>(path);
        Assert.AreEqual(0L, empty!.Revision); Assert.IsNull(empty.LeaderUserId);
        async Task<WorkflowOrganizationLeaderResolution> Resolve(Guid unitId, Guid? tenantId = null)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(new(tenantId ?? tenant.Id, tenant.Identifier, tenant.Name));
            return await scope.ServiceProvider.GetRequiredService<IWorkflowOrganizationAssigneeDirectory>().ResolveLeaderSourceAsync(actor, new(WorkflowOrganizationLeaderSourceKind.Unit, unitId));
        }
        var source = await Resolve(id); Assert.IsTrue(source.SourceIsValid); Assert.HasCount(0, source.Leaders);
        Assert.IsFalse((await Resolve(Guid.CreateVersion7())).SourceIsValid);
        Assert.IsFalse((await Resolve(id, Guid.CreateVersion7())).SourceIsValid);
        using (var invalid = await client.PutAsJsonAsync(path, new SetOrganizationUnitLeadershipRequest(Guid.CreateVersion7(), version, 0))) Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var stale = await client.PutAsJsonAsync(path, new SetOrganizationUnitLeadershipRequest(actor, version + 1, 0))) Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        var writes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PutAsJsonAsync(path, new SetOrganizationUnitLeadershipRequest(actor, version, 0))));
        try { Assert.HasCount(1, writes.Where(value => value.StatusCode == HttpStatusCode.OK).ToArray()); Assert.HasCount(1, writes.Where(value => value.StatusCode == HttpStatusCode.Conflict).ToArray()); }
        finally { foreach (var response in writes) response.Dispose(); }
        var configured = await client.GetFromJsonAsync<OrganizationUnitLeadershipResponse>(path);
        Assert.AreEqual(actor, configured!.LeaderUserId); Assert.AreEqual(actor, configured.UpdatedById); Assert.AreEqual(1L, configured.Revision);
        source = await Resolve(id); Assert.AreEqual(actor, source.Leaders.Single().UserId);
        var outsider = await factory.CreateHostIdentityAsync("leadership-outsider-" + Guid.NewGuid().ToString("N"), []);
        using (var host = factory.CreateClientForHost("localhost"))
        {
            host.DefaultRequestHeaders.Authorization = new("Bearer", outsider.AccessToken);
            using var denied = await host.GetAsync(path); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var cleared = await client.PutAsJsonAsync(path, new SetOrganizationUnitLeadershipRequest(null, version, 1))) Assert.AreEqual(HttpStatusCode.OK, cleared.StatusCode, await cleared.Content.ReadAsStringAsync());
        source = await Resolve(id); Assert.IsTrue(source.SourceIsValid); Assert.HasCount(0, source.Leaders);
        Assert.AreEqual(2L, (await client.GetFromJsonAsync<OrganizationUnitLeadershipResponse>(path))!.Revision);
        var check = "ck_fn_organization_unit_leadership_state";
        await db.ExecuteAsync($"ALTER TABLE fn_organization_unit_leadership DROP {(provider == DatabaseProvider.SqlServer ? "CONSTRAINT" : "FOREIGN KEY")} fk_fn_organization_unit_leadership_unit");
        await db.ExecuteAsync($"ALTER TABLE fn_organization_unit_leadership DROP {(provider == DatabaseProvider.SqlServer ? "CONSTRAINT" : "CHECK")} {check}");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%307_OrganizationUnitLeadership.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%307_OrganizationUnitLeadership.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider, connection);
        Assert.AreEqual(1, provider == DatabaseProvider.SqlServer
            ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE name=@check", new { check })
            : await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME=@check AND CONSTRAINT_TYPE='CHECK'", new { check }));
        Assert.AreEqual(2L, (await client.GetFromJsonAsync<OrganizationUnitLeadershipResponse>(path))!.Revision);
        Assert.AreEqual(1, provider == DatabaseProvider.SqlServer
            ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE name='fk_fn_organization_unit_leadership_unit'")
            : await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_organization_unit_leadership_unit'"));
    }
}
