using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    // 复用现有双库 CRUD fixture，增加真实认证/数据范围和绑定 JSON 断言，不另建慢测数据库。
    private static async Task VerifyApprovalProgressReadBoundaryAsync(FullNetApiFactory factory, HttpClient client,
        string adminToken, EnterpriseRequestResponse draft, CancellationToken ct)
    {
        var progress = await ReadProgress(client, adminToken, draft.Id, ct);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.NotSubmitted, progress.DeliveryState);
        Assert.IsNull(progress.WorkflowInstanceId);
        using var anonymous = await client.GetAsync($"{BasePath}/{draft.Id:D}/approval-progress", ct);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        foreach (var mode in new[] { "host", "no_read", "self", "repair_self" })
        {
            var permissions = new List<string> { "tenancy.tenants.read", "tenancy.tenants.switch" };
            if (mode != "no_read") permissions.Add(EnterpriseRequestPermissions.Read);
            if (mode is "no_read" or "repair_self") permissions.Add(EnterpriseRequestWorkflowPermissions.RepairApproval);
            var identity = await factory.CreateHostIdentityAsync($"progress-{Guid.NewGuid():N}", permissions, ct);
            if (mode.EndsWith("self", StringComparison.Ordinal))
            {
                await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
                    ? new SqlConnection(factory.ConnectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(factory.ConnectionString);
                await connection.OpenAsync(ct);
                // 身份第一次业务读取之前固定 self 范围，用户未归属草稿机构，不能读取其审批日志。
                Assert.AreEqual(1, await connection.ExecuteAsync("""
                    UPDATE fn_identity_role SET DataScopeKind = @DataScopeKind, Version = Version + 1
                    WHERE Id IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId = @UserId)
                    """, new { identity.UserId, DataScopeKind = RoleDataScopeKinds.Self }));
            }
            var token = identity.AccessToken;
            if (mode != "host")
            {
                using var enter = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
                { Content = JsonContent.Create(new ChangeTenantContextRequest(draft.TenantId)) };
                enter.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var entered = await client.SendAsync(enter, ct);
                Assert.AreEqual(HttpStatusCode.OK, entered.StatusCode, await entered.Content.ReadAsStringAsync(ct));
                token = (await entered.Content.ReadFromJsonAsync<TenantContextTokenResponse>(ct))!.AccessToken;
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{draft.Id:D}/approval-progress");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // 普通请求头不能覆盖可信租户或授予机构读取范围。
            request.Headers.Add("X-FullNet-Tenant-Id", draft.TenantId.ToString("D"));
            request.Headers.Add("X-FullNet-Organization-Unit-Id", draft.OrganizationUnitId.ToString("D"));
            using var denied = await client.SendAsync(request, ct);
            Assert.AreEqual(mode == "no_read" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, denied.StatusCode,
                await denied.Content.ReadAsStringAsync(ct));
            using var repairRequest = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{draft.Id:D}/repair-approval")
            { Content = JsonContent.Create(new RepairEnterpriseRequestApprovalRequest(Guid.NewGuid(), 2, "权限验证")) };
            repairRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var deniedRepair = await client.SendAsync(repairRequest, ct);
            Assert.AreEqual(mode == "repair_self" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, deniedRepair.StatusCode,
                "恢复需同时授权读取和恢复，且不能绕过申请数据范围。");
            using var lines = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{draft.Id:D}/lines");
            lines.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            lines.Headers.Add("X-FullNet-Tenant-Id", draft.TenantId.ToString("D"));
            using var hiddenLines = await client.SendAsync(lines, ct);
            Assert.AreEqual(mode == "no_read" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, hiddenLines.StatusCode);
            using var attachments = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{draft.Id:D}/attachments");
            attachments.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            attachments.Headers.Add("X-FullNet-Tenant-Id", draft.TenantId.ToString("D"));
            using var hiddenAttachments = await client.SendAsync(attachments, ct);
            Assert.AreEqual(mode == "no_read" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, hiddenAttachments.StatusCode);
            if (mode != "host")
            {
                using var writeLines = new HttpRequestMessage(HttpMethod.Put, $"{BasePath}/{draft.Id:D}/lines")
                { Content = JsonContent.Create(new { version = draft.Version.ToString(), items = Array.Empty<object>() }) };
                writeLines.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var deniedWrite = await client.SendAsync(writeLines, ct);
                Assert.AreEqual(HttpStatusCode.Forbidden, deniedWrite.StatusCode, "Read 权限不能授予明细写入。");
            }
        }
    }

    private static async Task<EnterpriseRequestApprovalProgressResponse> ReadProgress(HttpClient client, string token, Guid requestId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{requestId:D}/approval-progress");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        var progress = await response.Content.ReadFromJsonAsync<EnterpriseRequestApprovalProgressResponse>(ct);
        Assert.IsNotNull(progress);
        Assert.AreEqual(requestId, progress.RequestId);
        return progress;
    }
}
