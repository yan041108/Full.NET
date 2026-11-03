using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

/// <summary>企业流程申请：与工作流引导的 API 烟雾。</summary>
internal static class EnterpriseRequestAssertions
{
    private const string BasePath = "/api/v1/enterprise_request/enterprise-requests";
    private const string ImportTasksPath = "/api/v1/import-export/tasks";

    public static async Task VerifyTenantCrudContractAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var organizationUnitId = await CreateOrganizationUnitAsync(client, token, cancellationToken);

        var applicantUserId = Guid.NewGuid();
        var createBody = new
        {
            requestNumber = $"REQ-{Guid.NewGuid():N}".Substring(0, 16),
            title = "Integration probe",
            status = EnterpriseRequestStatusKeys.Draft,
            totalAmount = 100.50m,
            applicantUserId,
        };

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, BasePath)
        {
            Content = JsonContent.Create(createBody),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createRequest.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, organizationUnitId.ToString("D"));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode, await createResponse.Content.ReadAsStringAsync(cancellationToken));
        var created = await createResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(EnterpriseRequestStatusKeys.Draft, created!.Status);
        Assert.AreEqual(organizationUnitId, created.OrganizationUnitId);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{created.Id:D}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);

        using var submitRequest = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{created.Id:D}/submit-for-approval");
        submitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var submitResponse = await client.SendAsync(submitRequest, cancellationToken);
        Assert.IsTrue(
            submitResponse.StatusCode == HttpStatusCode.Forbidden,
            await submitResponse.Content.ReadAsStringAsync(cancellationToken));
    }

    public static async Task VerifyTenantSubmitForApprovalWhenDefinitionPublishedAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(
            client,
            token,
            cancellationToken);
        var organizationUnitId = await CreateOrganizationUnitAsync(client, token, cancellationToken);

        var createBody = new
        {
            requestNumber = $"REQ-{Guid.NewGuid():N}".Substring(0, 16),
            title = "Submit integration probe",
            status = EnterpriseRequestStatusKeys.Draft,
            totalAmount = 42m,
            applicantUserId = await GetCurrentUserIdAsync(client, token, cancellationToken),
        };

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, BasePath)
        {
            Content = JsonContent.Create(createBody),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createRequest.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, organizationUnitId.ToString("D"));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode, await createResponse.Content.ReadAsStringAsync(cancellationToken));
        var created = await createResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(cancellationToken);
        Assert.IsNotNull(created);

        using var submitRequest = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{created!.Id:D}/submit-for-approval");
        submitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var submitResponse = await client.SendAsync(submitRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, submitResponse.StatusCode, await submitResponse.Content.ReadAsStringAsync(cancellationToken));
        var submitted = await submitResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(cancellationToken);
        Assert.IsNotNull(submitted);
        Assert.AreEqual(EnterpriseRequestStatusKeys.Submitted, submitted!.Status);
        // 重复 HTTP 提交只返回原状态，不产生第二条启动意图。
        using var retry = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{created.Id:D}/submit-for-approval");
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var retried = await client.SendAsync(retry, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, retried.StatusCode);
        await using DbConnection db = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(factory.ConnectionString));
        const string messageType = "demo.enterprise_request.request.submitted";
        var envelopes = (await db.QueryAsync<SubmissionEnvelope>(
            "SELECT Id, TenantId, Payload FROM fn_outbox_message WHERE MessageType=@messageType", new { messageType })).ToArray();
        Assert.AreEqual(1, envelopes.Length);
        var envelope = envelopes[0];
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var resolver = scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>();
        writer.SetTenant((await resolver.ResolveActiveByIdAsync(envelope.TenantId, cancellationToken))!);
        var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler>().Single(item => item.EventType == messageType);
        var context = new IntegrationEventContext(envelope.Id, messageType, 1, envelope.TenantId, null, DateTimeOffset.UtcNow);
        await handler.HandleAsync(context, envelope.Payload, cancellationToken);
        await handler.HandleAsync(context, envelope.Payload, cancellationToken);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_workflow_instance WHERE BusinessType=@businessType AND BusinessId=@id",
            new { businessType = EnterpriseRequestWorkflowConstants.BusinessType, id = created.Id.ToString("D") }));
        writer.Clear();
    }

    public static async Task VerifyTenantDemoEnterpriseRequestsXlsxImportAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var organizationUnitId = await CreateOrganizationUnitAsync(client, token, cancellationToken);
        var applicantUserId = Guid.NewGuid();
        var requestNumber = $"IMP-{Guid.NewGuid():N}".Substring(0, 16);
        // 使用正式渲染契约生成 XLSX；不能把 CSV 改扩展名充当工作簿验收。
        var renderer = factory.Services.GetRequiredService<IReportingWorkbookRenderer>();
        string[] headers = ["requestNumber", "title", "totalAmount", "applicantUserId", "organizationUnitId"];
        var workbook = renderer.Render(headers.Select(header => new ReportingExecutionColumnDefinition(header, header)).ToArray(),
            [new ReportingExecutionRow(new Dictionary<string, string?>
            {
                ["requestNumber"] = requestNumber, ["title"] = "Imported row", ["totalAmount"] = "12.50",
                ["applicantUserId"] = applicantUserId.ToString("D"), ["organizationUnitId"] = organizationUnitId.ToString("D"),
            })]);
        Assert.IsTrue(workbook.IsSuccess);

        using var createContent = new MultipartFormDataContent
        {
            { new StringContent(EnterpriseRequestImportSchemaKeys.EnterpriseRequests), "schemaKey" },
            { new StringContent("requests"), "worksheetKey" },
        };
        var fileContent = new ByteArrayContent(workbook.Value!);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        createContent.Add(fileContent, "file", "demo-enterprise-requests.xlsx");

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, ImportTasksPath)
        {
            Content = createContent,
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode, await createResponse.Content.ReadAsStringAsync(cancellationToken));
        var created = await createResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(ImportExportTaskStatusKeys.PreviewSucceeded, created!.StatusKey);
        Assert.AreEqual(EnterpriseRequestImportSchemaKeys.EnterpriseRequests, created.SchemaKey);
        Assert.AreEqual(1, created.TotalRows);
        Assert.AreEqual(1, created.ValidRowCount);
        Assert.AreEqual(0, created.InvalidRowCount);

        using var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"{ImportTasksPath}/{created.Id:D}/execute");
        executeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var executeResponse = await client.SendAsync(executeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, executeResponse.StatusCode, await executeResponse.Content.ReadAsStringAsync(cancellationToken));
        var executed = await executeResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>(cancellationToken);
        Assert.IsNotNull(executed);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, executed!.StatusKey);
        Assert.IsTrue(executed.SucceededRowCount >= 1);

        // 模拟业务已提交但任务检查点丢失：以原可信任务/文件/主体直接重放处理器。
        await using (var replayScope = factory.Services.CreateAsyncScope())
        {
            var tenantWriter = replayScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            var tenantResolver = replayScope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>();
            tenantWriter.SetTenant((await tenantResolver.ResolveActiveByIdAsync(created.TenantId, cancellationToken))!);
            var handler = replayScope.ServiceProvider.GetServices<IStaticImportSchemaHandler>()
                .Single(value => value.SchemaKey == created.SchemaKey);
            var replayContext = new StaticImportPreviewContext(created.RequestedByUserId, new Dictionary<string, bool>())
            { TaskId = created.Id, SourceFileId = created.SourceFileId };
            Guid? replayedId = null;
            for (var iteration = 0; iteration < 2; iteration++)
            {
                using var input = new MemoryStream(workbook.Value!);
                var replayed = await handler.ExecuteBatchAsync(input, input.Length, 0, 50, replayContext, cancellationToken);
                Assert.IsTrue(replayed.IsSuccess);
                Assert.AreEqual(1, replayed.Value!.Rows.Count);
                Assert.IsTrue(replayed.Value.Rows[0].Succeeded);
                replayedId ??= replayed.Value.Rows[0].EntityId;
                Assert.AreEqual(replayedId, replayed.Value.Rows[0].EntityId);
            }
            using var changedInput = new MemoryStream(workbook.Value!);
            var changed = await handler.ExecuteBatchAsync(changedInput, changedInput.Length, 0, 50,
                replayContext with { SourceFileId = Guid.CreateVersion7() }, cancellationToken);
            Assert.IsTrue(changed.IsSuccess);
            Assert.IsFalse(changed.Value!.Rows[0].Succeeded);
            Assert.AreEqual("enterprise_request.import.row_conflict", changed.Value.Rows[0].ErrorCode);
        }

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}?page=1&pageSize=50");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(cancellationToken));
        var found = listJson.RootElement.GetProperty("items")
            .EnumerateArray()
            .Count(item => string.Equals(
                item.GetProperty("requestNumber").GetString(),
                requestNumber,
                StringComparison.Ordinal));
        Assert.AreEqual(1, found, "Replayed import must leave exactly one enterprise request.");
    }

    private sealed class SubmissionEnvelope
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public byte[] Payload { get; set; } = [];
    }

    internal static async Task<Guid> CreateOrganizationUnitAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        var code = $"er-{Guid.NewGuid():N}".ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/organization/units")
        {
            Content = JsonContent.Create(new CreateOrganizationUnitRequest(null, code, "ER Test Unit", 10)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var unitId = json.RootElement.GetProperty("id").GetGuid();
        var actorUserId = await GetCurrentUserIdAsync(client, token, cancellationToken);
        await AssignUserToOrganizationUnitAsync(client, token, actorUserId, unitId, cancellationToken);
        return unitId;
    }

    internal static async Task<Guid> GetCurrentUserIdAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AssignUserToOrganizationUnitAsync(
        HttpClient client,
        string token,
        Guid userId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/organization/user-units")
        {
            Content = JsonContent.Create(new CreateOrganizationUserUnitRequest(userId, unitId, false)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    internal static async Task<string> LoginAndEnterAcmeTenantAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(loginToken);

        using var availableRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginToken!.AccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        var available = await availableResponse.Content.ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        var acme = available!.Single(tenant => tenant.Identifier == "acme");

        using var enterRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(acme.Id)),
        };
        enterRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginToken.AccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        var entered = await enterResponse.Content.ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        return entered!.AccessToken;
    }
}
