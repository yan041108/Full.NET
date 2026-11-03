using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

/// <summary>普通活动成员通过真实邀请和角色授权操作样例，核对部门、租户、明细与文档金额边界。</summary>
[TestClass]
public sealed class EnterpriseRequestAccessMatrixTests
{
    private const string Root = "/api/v1/enterprise_request/enterprise-requests";

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Active_members_respect_department_tenant_detail_and_document_permissions(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection,
            new Dictionary<string, string?> { ["FullNet:ImportExport:RunSynchronously"] = "true" }, includeEnterpriseRequest: true);
        await factory.InitializeAsync();
        using var administrator = factory.CreateClientForHost("localhost");
        var administratorToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(administrator, "admin");
        administrator.DefaultRequestHeaders.Authorization = new("Bearer", administratorToken);
        var tenants = (await administrator.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!;
        var tenant = tenants.Single(value => value.Identifier == "acme");
        using var owner = factory.CreateClientForHost("localhost");
        // 单客户端登录策略会撤销旧会话，各准备阶段必须使用当前作用域的最新令牌。
        var ownerHostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin");
        var ownerToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, ownerHostToken, tenant.Id);
        owner.DefaultRequestHeaders.Authorization = new("Bearer", ownerToken);
        var unitA = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(owner, ownerToken, default);
        var unitB = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(owner, ownerToken, default);
        await IdentityOrganizationUnitProjectionTestHelper.BackfillTenantAsync(factory, tenant.Id);

        var writer = await PrepareMemberAsync(factory, owner, [EnterpriseRequestPermissions.Read, EnterpriseRequestPermissions.Create,
            EnterpriseRequestPermissions.Update, EnterpriseRequestDetailPermissions.Read, EnterpriseRequestDeliveryPermissions.Print,
            "import_export.import_tasks.read", "import_export.import_tasks.create", "import_export.import_tasks.execute", "import_export.static_schemas.read"]);
        var reader = await PrepareMemberAsync(factory, owner, [EnterpriseRequestPermissions.Read, EnterpriseRequestDeliveryPermissions.Print]);
        await AssignAsync(writer.UserId, unitA); await AssignAsync(reader.UserId, unitB);
        await RestrictAsync(writer.UserId, unitA); await RestrictAsync(reader.UserId, unitB);
        using var member = factory.CreateClientForHost("localhost");
        var memberToken = await EnterMemberAsync(member, writer);
        member.DefaultRequestHeaders.Authorization = new("Bearer", memberToken);
        var profile = await member.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.IsFalse(profile.GetProperty("isSuperAdministrator").GetBoolean());

        var visible = await SendAsync(member, HttpMethod.Post, Root + "/aggregate", Create(writer.UserId, "Member A"), HttpStatusCode.OK, unitA);
        var visibleMaster = visible.GetProperty("master"); var visibleId = visibleMaster.GetProperty("id").GetGuid();
        owner.DefaultRequestHeaders.Authorization = null;
        ownerHostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin");
        ownerToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, ownerHostToken, tenant.Id);
        owner.DefaultRequestHeaders.Authorization = new("Bearer", ownerToken);
        var hidden = await SendAsync(owner, HttpMethod.Post, Root + "/aggregate", Create(reader.UserId, "Member B"), HttpStatusCode.OK, unitB);
        var hiddenId = hidden.GetProperty("master").GetProperty("id").GetGuid();
        var directory = await SendAsync(member, HttpMethod.Get, Root + "?page=1&pageSize=20", null, HttpStatusCode.OK);
        var items = directory.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(1, items.Length); Assert.AreEqual(visibleId, items[0].GetProperty("id").GetGuid());
        var discovered = await SendAsync(member, HttpMethod.Get, Root + "/search?q=Member", null, HttpStatusCode.OK);
        Assert.AreEqual(1, discovered.GetProperty("items").GetArrayLength());
        Assert.AreEqual(visibleId, discovered.GetProperty("items")[0].GetProperty("id").GetGuid());
        await SendAsync(member, HttpMethod.Get, $"{Root}/{hiddenId:D}", null, HttpStatusCode.NotFound);
        await SendAsync(member, HttpMethod.Get, $"{Root}/{hiddenId:D}/aggregate", null, HttpStatusCode.NotFound);
        await SendAsync(member, HttpMethod.Post, Root + "/aggregate", Create(writer.UserId, "Forbidden unit"), HttpStatusCode.Forbidden, unitB);
        await SendAsync(member, HttpMethod.Put, $"{Root}/{visibleId:D}",
            new UpdateEnterpriseRequestRequest("MATRIX", "Stale version", "Draft", 12345.67m, writer.UserId, 999), HttpStatusCode.Conflict);
        var details = await SendAsync(member, HttpMethod.Get, $"{Root}/{visibleId:D}/aggregate", null, HttpStatusCode.OK);
        Assert.AreEqual(1, details.GetProperty("details").GetArrayLength());
        Assert.AreEqual(writer.UserId, details.GetProperty("master").GetProperty("applicantUserId").GetGuid());
        // 文档金额是独立交付权限；主单读取不会隐式授予金额投影。
        var print = await SendAsync(member, HttpMethod.Post, $"{Root}/{visibleId:D}/print", null, HttpStatusCode.OK);
        Assert.IsFalse(print.GetProperty("includesAmounts").GetBoolean());
        Assert.IsFalse(print.GetProperty("html").GetString()!.Contains("12345.67", StringComparison.Ordinal));
        using var readOnlyMember = factory.CreateClientForHost("localhost");
        readOnlyMember.DefaultRequestHeaders.Authorization = new("Bearer", await EnterMemberAsync(readOnlyMember, reader));
        await SendAsync(readOnlyMember, HttpMethod.Get, $"{Root}/{hiddenId:D}", null, HttpStatusCode.OK);
        await SendAsync(readOnlyMember, HttpMethod.Get, $"{Root}/{hiddenId:D}/aggregate", null, HttpStatusCode.Forbidden);
        await SendAsync(readOnlyMember, HttpMethod.Post, $"{Root}/{hiddenId:D}/print", null, HttpStatusCode.Forbidden);
        await VerifyMemberImportAsync(factory, member, owner, writer.UserId, unitA, unitB);
        using var other = factory.CreateClientForHost("localhost");
        var otherHostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(other, "admin");
        other.DefaultRequestHeaders.Authorization = new("Bearer", otherHostToken);
        var identifier = "matrix-" + Guid.CreateVersion7().ToString("N")[..16];
        var otherTenant = await SendAsync(other, HttpMethod.Post, "/api/v1/tenancy/tenants",
            new ProvisionTenantRequest(identifier, "Isolation tenant", identifier + ".localhost"), HttpStatusCode.Created);
        other.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(other, otherHostToken, otherTenant.GetProperty("id").GetGuid()));
        await SendAsync(other, HttpMethod.Get, $"{Root}/{visibleId:D}", null, HttpStatusCode.NotFound);
        await SendAsync(other, HttpMethod.Get, $"{Root}/{visibleId:D}/aggregate", null, HttpStatusCode.NotFound);
        Assert.AreEqual(0, (await SendAsync(other, HttpMethod.Get, Root + "/search?q=Member", null, HttpStatusCode.OK)).GetProperty("items").GetArrayLength());

        async Task AssignAsync(Guid user, Guid unit) => _ = await SendAsync(owner, HttpMethod.Post, "/api/v1/organization/user-units",
            new CreateOrganizationUserUnitRequest(user, unit, true), HttpStatusCode.Created);
        async Task RestrictAsync(Guid user, Guid unit)
        {
            administrator.DefaultRequestHeaders.Authorization = null;
            administrator.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(administrator, "admin"));
            var roles = (await administrator.GetFromJsonAsync<HostUserRolesResponse>($"/api/v1/identity/users/{user:D}/roles"))!;
            var role = (await administrator.GetFromJsonAsync<HostRoleResponse>($"/api/v1/identity/roles/{roles.RoleIds.Single():D}"))!;
            await SendAsync(administrator, HttpMethod.Put, $"/api/v1/identity/roles/{role.Id:D}/data-scope",
                new UpdateHostRoleDataScopeRequest(RoleDataScopeKinds.Custom, [unit], role.Version, tenant.Id), HttpStatusCode.OK);
        }
        async Task<string> EnterMemberAsync(HttpClient client, HostTestIdentity identity)
        {
            var host = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, identity.Username);
            return await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, host, tenant.Id);
        }
    }

    private static CreateEnterpriseRequestAggregateRequest Create(Guid applicant, string title) => new(
        new("MATRIX-" + Guid.CreateVersion7().ToString("N")[..16], title, "Draft", 12345.67m, applicant),
        [new(1, "Member detail", 1m, 12345.67m, 12345.67m)]);

    private static async Task VerifyMemberImportAsync(FullNetApiFactory factory, HttpClient member, HttpClient owner, Guid applicant, Guid allowed, Guid forbidden)
    {
        var renderer = factory.Services.GetRequiredService<IReportingWorkbookRenderer>();
        string[] headers = ["requestNumber", "title", "totalAmount", "applicantUserId", "organizationUnitId"];
        string? rejectedNumber = null;
        foreach (var unit in new[] { allowed, forbidden })
        {
            var number = "MEM-IMP-" + Guid.CreateVersion7().ToString("N")[..16];
            var workbook = renderer.Render(headers.Select(key => new ReportingExecutionColumnDefinition(key, key)).ToArray(),
                [new ReportingExecutionRow(new Dictionary<string, string?> { ["requestNumber"] = number, ["title"] = "Member import",
                    ["totalAmount"] = "12.50", ["applicantUserId"] = applicant.ToString("D"), ["organizationUnitId"] = unit.ToString("D") })]);
            Assert.IsTrue(workbook.IsSuccess);
            using var content = new MultipartFormDataContent { { new StringContent(EnterpriseRequestImportSchemaKeys.EnterpriseRequests), "schemaKey" },
                { new StringContent("requests"), "worksheetKey" } };
            var file = new ByteArrayContent(workbook.Value!); file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Add(file, "file", "member.xlsx");
            using var created = await member.PostAsync("/api/v1/import-export/tasks", content);
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
            var task = await created.Content.ReadFromJsonAsync<JsonElement>(); Assert.AreEqual(1, task.GetProperty("validRowCount").GetInt32());
            var executed = await SendAsync(member, HttpMethod.Post, $"/api/v1/import-export/tasks/{task.GetProperty("id").GetGuid():D}/execute", null, HttpStatusCode.OK);
            Assert.AreEqual(unit == allowed ? 1 : 0, executed.GetProperty("succeededRowCount").GetInt32());
            Assert.AreEqual(unit == forbidden ? 1 : 0, executed.GetProperty("executionFailedRowCount").GetInt32());
            if (unit == forbidden) rejectedNumber = number;
        }
        var all = await SendAsync(owner, HttpMethod.Get, Root + "?page=1&pageSize=20", null, HttpStatusCode.OK);
        Assert.IsFalse(all.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("requestNumber").GetString() == rejectedNumber));
        var mine = await SendAsync(member, HttpMethod.Get, Root + "?page=1&pageSize=20", null, HttpStatusCode.OK);
        Assert.AreEqual(2, mine.GetProperty("items").GetArrayLength());
    }

    private static async Task<HostTestIdentity> PrepareMemberAsync(FullNetApiFactory factory, HttpClient owner, IReadOnlyList<string> permissions)
    {
        var name = "enterprise.member." + Guid.CreateVersion7().ToString("N");
        var identity = await factory.CreateHostIdentityAsync(name, [.. permissions, "tenancy.tenants.read", "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        var email = name + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, name);
        var invitation = await SendAsync(owner, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations",
            new CreateTenantInvitationRequest(email, TenantMemberRoles.Member), HttpStatusCode.OK);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, name));
        await SendAsync(client, HttpMethod.Post, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }, HttpStatusCode.OK);
        return identity;
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected, Guid? unit = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (unit is { } id) request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, id.ToString("D"));
        using var response = await client.SendAsync(request); var content = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, path + Environment.NewLine + content);
        using var result = JsonDocument.Parse(content); return result.RootElement.Clone();
    }
}
