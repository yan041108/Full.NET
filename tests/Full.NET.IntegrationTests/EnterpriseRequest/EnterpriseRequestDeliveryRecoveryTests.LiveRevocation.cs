using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.Delivery;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.TenantResourceFiles;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

public sealed partial class EnterpriseRequestDeliveryRecoveryTests
{
    [TestMethod]
    public Task Live_role_revocation_and_historical_download_with_sql_server() => VerifyLiveRevocationAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task Live_role_revocation_and_historical_download_with_mysql() => VerifyLiveRevocationAsync(DatabaseProvider.MySql);

    private static async Task VerifyLiveRevocationAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var uploads = new UploadFault();
        using var factory = new FullNetApiFactory(provider, connection,
            new Dictionary<string, string?> { ["EnterpriseRequest:Delivery:Enabled"] = "false" },
            configureTestServices: services =>
            {
                // 只观察真实 Files 上传次数，不替换字段、角色或会话授权器。
                services.AddScoped<TenantResourceFileStore>();
                services.Replace(ServiceDescriptor.Scoped<ITenantResourceFileStore>(sp => new LostUploadStore(sp.GetRequiredService<TenantResourceFileStore>(), uploads)));
            }, includeEnterpriseRequest: true);
        await factory.InitializeAsync();
        using var owner = factory.CreateClientForHost("localhost");
        var ownerHost = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", ownerHost);
        var tenant = (await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(value => value.Identifier == "acme");
        var ownerTenant = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, ownerHost, tenant.Id);
        owner.DefaultRequestHeaders.Authorization = new("Bearer", ownerTenant);
        var unit = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(owner, ownerTenant, default);
        await IdentityOrganizationUnitProjectionTestHelper.BackfillTenantAsync(factory, tenant.Id);
        string[] permissions = [EnterpriseRequestPermissions.Read, EnterpriseRequestPermissions.Create, EnterpriseRequestDetailPermissions.Read,
            EnterpriseRequestDeliveryPermissions.ReportsCreate, EnterpriseRequestDeliveryPermissions.ReportsRead, EnterpriseRequestDeliveryPermissions.AmountsRead,
            "tenancy.tenants.read", "tenancy.tenants.switch"];
        var username = "delivery.live." + Guid.CreateVersion7().ToString("N");
        var identity = await factory.CreateHostIdentityAsync(username, permissions, password: FullNetApiFactory.TestPassword);
        var email = username + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, username);
        var invitation = await SendAsync(owner, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member), HttpStatusCode.OK);
        using var member = factory.CreateClientForHost("localhost");
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, username));
        await SendAsync(member, HttpMethod.Post, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }, HttpStatusCode.OK);
        await SendAsync(owner, HttpMethod.Post, "/api/v1/organization/user-units", new CreateOrganizationUserUnitRequest(identity.UserId, unit, true), HttpStatusCode.Created);
        using var administrator = factory.CreateClientForHost("localhost");
        administrator.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(administrator, "admin"));
        var assignments = (await administrator.GetFromJsonAsync<HostUserRolesResponse>($"/api/v1/identity/users/{identity.UserId:D}/roles"))!;
        var roleId = assignments.RoleIds.Single(); var role = (await ReadRoleAsync())!;
        await SendAsync(administrator, HttpMethod.Put, $"/api/v1/identity/roles/{roleId:D}/data-scope", new UpdateHostRoleDataScopeRequest(RoleDataScopeKinds.Custom, [unit], role.Version, tenant.Id), HttpStatusCode.OK);
        await SignInAsync();
        Assert.IsFalse((await member.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("isSuperAdministrator").GetBoolean());
        var document = await SendAsync(member, HttpMethod.Post, "/api/v1/enterprise_request/enterprise-requests/aggregate",
            new CreateEnterpriseRequestAggregateRequest(new("LIVE-" + Guid.CreateVersion7().ToString("N")[..16], "权限交付", "Draft", 98765.43m, identity.UserId),
                [new(1, "受控明细", 1m, 98765.43m, 98765.43m)]), HttpStatusCode.OK, unit);
        var master = document.GetProperty("master"); var requestId = master.GetProperty("id").GetGuid(); var sourceVersion = master.GetProperty("version").GetString()!;
        var path = $"/api/v1/enterprise_request/enterprise-requests/{requestId:D}";
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        await db.OpenAsync();
        using var worker = new EnterpriseRequestDeliveryWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EnterpriseRequestDeliveryOptions { Enabled = true, IntervalSeconds = 1 }), factory.Services.GetRequiredService<EnterpriseRequestDeliveryScanCursor>(), NullLogger<EnterpriseRequestDeliveryWorker>.Instance);
        var ready = await CreateReportAsync(); await UntilAsync(ready, "Ready"); var original = await DownloadAsync(ready, HttpStatusCode.OK);
        Assert.AreEqual(1, uploads.Uploads);
        // 新交互会话可以读取本人既有结果，不能把原执行会话当作历史下载资格。
        await SignInAsync(); CollectionAssert.AreEqual(original, await DownloadAsync(ready, HttpStatusCode.OK));
        var pending = await CreateReportAsync();
        // 在真实文件已经打开后通过正式角色 API 撤权，最终交付必须再次拒绝。
        uploads.Opened = () => ReplacePermissionsAsync(permissions.Where(code => code != EnterpriseRequestDeliveryPermissions.AmountsRead).ToArray());
        await DownloadAsync(ready, HttpStatusCode.Forbidden);
        var before = uploads.Uploads; await UntilAsync(pending, "Failed"); Assert.AreEqual(before, uploads.Uploads);
        Assert.IsNull(await db.ExecuteScalarAsync<Guid?>("SELECT FileId FROM demo_enterprise_request_delivery WHERE Id=@Id", new { Id = pending }));
        await SignInAsync(); await DownloadAsync(ready, HttpStatusCode.Forbidden);
        var reduced = await CreateReportAsync(); await UntilAsync(reduced, "Ready"); var reducedBytes = await DownloadAsync(reduced, HttpStatusCode.OK);
        using (var archive = new ZipArchive(new MemoryStream(reducedBytes), ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries.Where(value => value.FullName.StartsWith("xl/", StringComparison.Ordinal) && value.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            { using var reader = new StreamReader(entry.Open()); Assert.IsFalse((await reader.ReadToEndAsync()).Contains("98765.43", StringComparison.Ordinal)); }
        }
        await ReplacePermissionsAsync(permissions); await SignInAsync();
        CollectionAssert.AreEqual(reducedBytes, await DownloadAsync(reduced, HttpStatusCode.OK));
        // 生成权限与读取权限独立，保留读取资格时仍可取得已就绪结果。
        var noCreate = await CreateReportAsync();
        await ReplacePermissionsAsync(permissions.Where(code => code != EnterpriseRequestDeliveryPermissions.ReportsCreate).ToArray()); await SignInAsync();
        await UntilAsync(noCreate, "Failed");
        CollectionAssert.AreEqual(reducedBytes, await DownloadAsync(reduced, HttpStatusCode.OK));
        Assert.AreEqual(2, uploads.Uploads);
        Assert.AreEqual("Failed", await db.ExecuteScalarAsync<string>("SELECT Phase FROM demo_enterprise_request_delivery WHERE Id=@Id", new { Id = pending }));

        Task<HostRoleResponse?> ReadRoleAsync() => administrator.GetFromJsonAsync<HostRoleResponse>($"/api/v1/identity/roles/{roleId:D}");
        async Task ReplacePermissionsAsync(string[] codes) { var currentRole = (await ReadRoleAsync())!; await SendAsync(administrator, HttpMethod.Put, $"/api/v1/identity/roles/{roleId:D}/permissions", new ReplaceHostRolePermissionsRequest(codes, currentRole.Version), HttpStatusCode.OK); }
        async Task SignInAsync() { member.DefaultRequestHeaders.Authorization = null; var host = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, username); member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, host, tenant.Id)); }
        async Task<Guid> CreateReportAsync() { var created = await SendAsync(member, HttpMethod.Post, path + "/reports", new { idempotencyKey = Guid.CreateVersion7(), requestVersion = sourceVersion }, HttpStatusCode.OK); return created.GetProperty("id").GetGuid(); }
        async Task<byte[]> DownloadAsync(Guid reportId, HttpStatusCode expected) { using var response = await member.GetAsync($"{path}/reports/{reportId:D}/download"); Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync()); return await response.Content.ReadAsByteArrayAsync(); }
        async Task UntilAsync(Guid reportId, string phase)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                await db.ExecuteAsync("UPDATE demo_enterprise_request_delivery SET DueAtUtc=@Now WHERE Id=@Id", new { Id = reportId, Now = DateTimeOffset.UtcNow.AddSeconds(-1) });
                await worker.RunBatchAsync(default);
                if (await db.ExecuteScalarAsync<string>("SELECT Phase FROM demo_enterprise_request_delivery WHERE Id=@Id", new { Id = reportId }) == phase) return;
            }
            Assert.Fail("Report did not reach " + phase);
        }
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object body, HttpStatusCode expected, Guid? unit = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (unit is { } id) request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, id.ToString("D"));
        using var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, text);
        using var document = JsonDocument.Parse(text); return document.RootElement.Clone();
    }
}
