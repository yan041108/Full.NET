using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    // 每库一个真实 API fixture，共享机构、角色和附件，避免为每个矩阵格重建数据库。
    public static async Task VerifySecurityMatrixAsync(FullNetApiFactory factory, CancellationToken ct = default)
    {
        await factory.InitializeAsync(ct);
        using var client = factory.CreateClientForHost("localhost");
        var hostAdmin = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin", cancellationToken: ct);
        var admin = await LoginAndEnterAcmeTenantAsync(client, ct);
        var adminId = await GetCurrentUserIdAsync(client, admin, ct);
        var unit = await CreateOrganizationUnitAsync(client, admin, ct);
        var otherUnit = await CreateOrganizationUnitAsync(client, admin, ct);
        var main = await Create(admin, unit, "同机构他人申请");
        var hidden = await Create(admin, otherUnit, "其他机构申请");
        var self = await Actor(RoleDataScopeKinds.Self, true);
        var department = await Actor(RoleDataScopeKinds.Organization, true);
        var all = await Actor(RoleDataScopeKinds.All, true);
        var noRead = await Actor(RoleDataScopeKinds.All, false);
        var mine = await Create(self.Token, unit, "本人创建但申请人代填");
        Assert.AreEqual(self.Id, mine.CreatedById);
        Assert.AreEqual(adminId, mine.ApplicantUserId, "代填申请人不能改变可信数据归属。");

        await Visible(self.Token, [mine.Id]);
        await Visible(department.Token, [main.Id, mine.Id]);
        await Visible(all.Token, [main.Id, hidden.Id, mine.Id]);
        using (var denied = await Send(noRead.Token, HttpMethod.Get, BasePath)) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(main.Version.ToString()), "version");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("private attachment")), "file", "scope.txt");
        using var upload = await Send(admin, HttpMethod.Post, $"{BasePath}/{main.Id:D}/attachments", content: form);
        Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode, await upload.Content.ReadAsStringAsync(ct));
        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync(ct));
        var attachmentId = uploaded.RootElement.GetProperty("attachment").GetProperty("id").GetGuid();
        var mainVersion = long.Parse(uploaded.RootElement.GetProperty("requestVersion").GetString()!);

        foreach (var path in new[] { "", "/lines", "/attachments", $"/attachments/{attachmentId:D}/content", "/approval-progress" })
        {
            using var denied = await Send(self.Token, HttpMethod.Get, $"{BasePath}/{main.Id:D}{path}", spoof: true);
            Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode, "本人范围不能通过机构请求头读取同机构他人单据：" + path);
            using var allowed = await Send(department.Token, HttpMethod.Get, $"{BasePath}/{main.Id:D}{path}");
            Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode, path);
            using var noPermission = await Send(noRead.Token, HttpMethod.Get, $"{BasePath}/{main.Id:D}{path}");
            Assert.AreEqual(HttpStatusCode.Forbidden, noPermission.StatusCode, path);
        }

        var identifier = "scope-" + Guid.NewGuid().ToString("N")[..10];
        using var createTenant = await Send(hostAdmin, HttpMethod.Post, "/api/v1/tenancy/tenants",
            new ProvisionTenantRequest(identifier, "隔离矩阵租户", identifier + ".localhost"));
        Assert.AreEqual(HttpStatusCode.Created, createTenant.StatusCode, await createTenant.Content.ReadAsStringAsync(ct));
        var tenantB = (await createTenant.Content.ReadFromJsonAsync<TenantSummary>(ct))!;
        var freshAdmin = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin", cancellationToken: ct);
        var adminB = await Enter(freshAdmin, tenantB.Id);
        // Host 管理员可管理租户，但不能被当成该租户的活动成员来绑定机构。
        using var createUnitB = await Send(adminB, HttpMethod.Post, "/api/v1/organization/units",
            new CreateOrganizationUnitRequest(null, "scope-b-" + Guid.NewGuid().ToString("N"), "B租户机构", 10));
        Assert.AreEqual(HttpStatusCode.Created, createUnitB.StatusCode, await createUnitB.Content.ReadAsStringAsync(ct));
        using var unitBJson = JsonDocument.Parse(await createUnitB.Content.ReadAsStringAsync(ct));
        var unitB = unitBJson.RootElement.GetProperty("id").GetGuid();
        var memberB = await Actor(RoleDataScopeKinds.All, true, adminB, tenantB.Id, unitB);
        var foreign = await Create(memberB.Token, unitB, "B租户申请", memberB.Id);
        await Visible(adminB, [foreign.Id]);
        await Visible(all.Token, [main.Id, hidden.Id, mine.Id]);
        foreach (var path in new[] { "", "/lines", "/attachments", $"/attachments/{attachmentId:D}/content", "/approval-progress" })
        {
            using var denied = await Send(adminB, HttpMethod.Get, $"{BasePath}/{main.Id:D}{path}", spoof: true);
            Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode, "超级管理员的 B 租户上下文也不能用请求头覆盖 A 租户：" + path);
        }
        using (var crossWrite = await Send(adminB, HttpMethod.Put, $"{BasePath}/{main.Id:D}", Update("越租户写入", mainVersion)))
            Assert.AreEqual(HttpStatusCode.NotFound, crossWrite.StatusCode);
        using (var crossDelete = await Send(adminB, HttpMethod.Delete, $"{BasePath}/{main.Id:D}/attachments/{attachmentId:D}", new { version = mainVersion.ToString() }))
            Assert.AreEqual(HttpStatusCode.NotFound, crossDelete.StatusCode);
        using (var crossLines = await Send(adminB, HttpMethod.Put, $"{BasePath}/{main.Id:D}/lines", new { version = mainVersion.ToString(), items = Array.Empty<object>() }))
            Assert.AreEqual(HttpStatusCode.NotFound, crossLines.StatusCode);
        using (var hiddenWrite = await Send(department.Token, HttpMethod.Put, $"{BasePath}/{hidden.Id:D}",
            new UpdateEnterpriseRequestRequest(hidden.RequestNumber, "越机构写入", "Draft", 0m, hidden.ApplicantUserId, hidden.Version), spoof: true))
            Assert.AreEqual(HttpStatusCode.Forbidden, hiddenWrite.StatusCode, "机构请求头不能替代记录原机构的写授权。");
        using (var unauthorizedUpload = await Send(noRead.Token, HttpMethod.Post, $"{BasePath}/{main.Id:D}/attachments", content: new MultipartFormDataContent()))
            Assert.AreEqual(HttpStatusCode.Forbidden, unauthorizedUpload.StatusCode);

        // 两个实际并发请求共用版本，必须恰好一个赢；同时尝试覆盖服务端所有权/审计字段。
        var concurrent = await Task.WhenAll(Send(admin, HttpMethod.Put, $"{BasePath}/{main.Id:D}", Update("并发甲", mainVersion)),
            Send(admin, HttpMethod.Put, $"{BasePath}/{main.Id:D}", Update("并发乙", mainVersion)));
        try
        {
            CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, concurrent.Select(value => value.StatusCode).ToArray());
            var winner = (await concurrent.Single(value => value.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<EnterpriseRequestResponse>(ct))!;
            Assert.AreEqual(main.TenantId, winner.TenantId);
            Assert.AreEqual(unit, winner.OrganizationUnitId);
            Assert.AreEqual(adminId, winner.CreatedById);
            Assert.AreEqual(main.CreatedAtUtc, winner.CreatedAtUtc);
            Assert.AreEqual(adminId, winner.UpdatedById);
            Assert.AreEqual(mainVersion + 1, winner.Version);
            Assert.IsFalse(winner.IsDeleted);
            using var read = await Send(admin, HttpMethod.Get, $"{BasePath}/{main.Id:D}");
            Assert.AreEqual(winner, await read.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(ct));
            using var staleDelete = await Send(admin, HttpMethod.Post, $"{BasePath}/{main.Id:D}/delete", new { version = mainVersion.ToString() });
            Assert.AreEqual(HttpStatusCode.Conflict, staleDelete.StatusCode);
            using var preserved = await Send(admin, HttpMethod.Get, $"{BasePath}/{main.Id:D}/attachments/{attachmentId:D}/content");
            Assert.AreEqual(HttpStatusCode.OK, preserved.StatusCode);
            Assert.AreEqual("private attachment", await preserved.Content.ReadAsStringAsync(ct));
        }
        finally { foreach (var response in concurrent) response.Dispose(); }

        object Update(string title, long version) => new
        {
            main.RequestNumber, title, status = "Draft", totalAmount = 0m, applicantUserId = adminId, version = version.ToString(),
            tenantId = tenantB.Id, organizationUnitId = otherUnit, createdById = self.Id, updatedById = self.Id,
            createdAtUtc = DateTimeOffset.UtcNow.AddYears(-1), isDeleted = true, deletedById = self.Id,
        };
        async Task<EnterpriseRequestResponse> Create(string token, Guid org, string title, Guid? applicantId = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BasePath)
            {
                Content = JsonContent.Create(new { requestNumber = "SEC-" + Guid.NewGuid().ToString("N")[..12], title, status = "Draft",
                    totalAmount = 0m, applicantUserId = applicantId ?? adminId, tenantId = Guid.NewGuid(), organizationUnitId = Guid.NewGuid(),
                    createdById = Guid.NewGuid(), isDeleted = true, version = 123 }),
            };
            request.Headers.Authorization = new("Bearer", token);
            request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, org.ToString("D"));
            using var response = await client.SendAsync(request, ct);
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            var row = (await response.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(ct))!;
            Assert.AreEqual(org, row.OrganizationUnitId); Assert.AreEqual(1L, row.Version); Assert.IsFalse(row.IsDeleted);
            return row;
        }
        async Task Visible(string token, Guid[] expected)
        {
            using var response = await Send(token, HttpMethod.Get, BasePath + "?page=1&pageSize=100");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            var page = (await response.Content.ReadFromJsonAsync<PagedResult<EnterpriseRequestResponse>>(ct))!;
            Assert.AreEqual((long)expected.Length, page.Total);
            CollectionAssert.AreEquivalent(expected, page.Items.Select(value => value.Id).ToArray());
        }
        async Task<(Guid Id, string Token)> Actor(string kind, bool read, string? tenantToken = null, Guid? tenantId = null, Guid? organizationId = null)
        {
            var directoryToken = tenantToken ?? admin;
            using var provision = await Send(directoryToken, HttpMethod.Post, "/api/v1/identity/tenant-members/provision",
                new ProvisionTenantMemberRequest("scope-" + Guid.NewGuid().ToString("N")[..12], "范围用户", FullNetApiFactory.TestPassword, "Member", null));
            Assert.AreEqual(HttpStatusCode.OK, provision.StatusCode, await provision.Content.ReadAsStringAsync(ct));
            var member = (await provision.Content.ReadFromJsonAsync<TenantMemberResponse>(ct))!;
            using var roleCreate = await Send(hostAdmin, HttpMethod.Post, "/api/v1/identity/roles", new CreateHostRoleRequest("scope-" + Guid.NewGuid().ToString("N"), "申请范围角色"));
            Assert.AreEqual(HttpStatusCode.Created, roleCreate.StatusCode);
            var role = (await roleCreate.Content.ReadFromJsonAsync<HostRoleResponse>(ct))!;
            var permissions = new List<string> { "tenancy.tenants.read", "tenancy.tenants.switch" };
            if (read) permissions.AddRange([EnterpriseRequestPermissions.Read, EnterpriseRequestPermissions.Create, EnterpriseRequestPermissions.Update, EnterpriseRequestPermissions.Disable]);
            using var grant = await Send(hostAdmin, HttpMethod.Put, $"/api/v1/identity/roles/{role.Id:D}/permissions", new ReplaceHostRolePermissionsRequest(permissions, role.Version));
            Assert.AreEqual(HttpStatusCode.OK, grant.StatusCode, await grant.Content.ReadAsStringAsync(ct));
            role = (await grant.Content.ReadFromJsonAsync<HostRoleResponse>(ct))!;
            using var scope = await Send(hostAdmin, HttpMethod.Put, $"/api/v1/identity/roles/{role.Id:D}/data-scope", new UpdateHostRoleDataScopeRequest(kind, null, role.Version));
            Assert.AreEqual(HttpStatusCode.OK, scope.StatusCode, await scope.Content.ReadAsStringAsync(ct));
            using var rolesGet = await Send(hostAdmin, HttpMethod.Get, $"/api/v1/identity/users/{member.UserId:D}/roles");
            var roles = (await rolesGet.Content.ReadFromJsonAsync<HostUserRolesResponse>(ct))!;
            using var assign = await Send(hostAdmin, HttpMethod.Put, $"/api/v1/identity/users/{member.UserId:D}/roles", new ReplaceHostUserRolesRequest([role.Id], roles.Version));
            Assert.AreEqual(HttpStatusCode.OK, assign.StatusCode, await assign.Content.ReadAsStringAsync(ct));
            using var link = await Send(directoryToken, HttpMethod.Post, "/api/v1/organization/user-units", new CreateOrganizationUserUnitRequest(member.UserId, organizationId ?? unit, true));
            Assert.AreEqual(HttpStatusCode.Created, link.StatusCode, await link.Content.ReadAsStringAsync(ct));
            var host = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, member.Username, cancellationToken: ct);
            return (member.UserId, await Enter(host, tenantId ?? main.TenantId));
        }
        async Task<string> Enter(string token, Guid tenantId)
        {
            using var response = await Send(token, HttpMethod.Put, "/api/v1/tenancy/context", new ChangeTenantContextRequest(tenantId));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            return (await response.Content.ReadFromJsonAsync<TenantContextTokenResponse>(ct))!.AccessToken;
        }
        async Task<HttpResponseMessage> Send(string token, HttpMethod method, string path, object? body = null, HttpContent? content = null, bool spoof = false)
        {
            using var request = new HttpRequestMessage(method, path) { Content = content ?? (body is null ? null : JsonContent.Create(body)) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (spoof)
            {
                request.Headers.Add("X-FullNet-Tenant-Id", main.TenantId.ToString("D"));
                request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D"));
            }
            return await client.SendAsync(request, ct);
        }
    }
}
