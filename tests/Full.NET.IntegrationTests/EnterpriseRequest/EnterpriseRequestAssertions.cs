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

    public static async Task VerifyTenantDemoEnterpriseRequestsCsvImportAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var organizationUnitId = await CreateOrganizationUnitAsync(client, token, cancellationToken);
        var applicantUserId = Guid.NewGuid();
        var requestNumber = $"IMP-{Guid.NewGuid():N}".Substring(0, 16);
        var csv = new StringBuilder()
            .AppendLine("requestNumber,title,totalAmount,applicantUserId,organizationUnitId")
            .Append(requestNumber)
            .Append(",Imported row,12.5,")
            .Append(applicantUserId.ToString("D", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(organizationUnitId.ToString("D", CultureInfo.InvariantCulture))
            .AppendLine()
            .ToString();
        var csvBytes = Encoding.UTF8.GetBytes(csv);

        using var createContent = new MultipartFormDataContent
        {
            { new StringContent(EnterpriseRequestImportSchemaKeys.EnterpriseRequests), "schemaKey" },
            { new StringContent("requests"), "worksheetKey" },
        };
        var fileContent = new ByteArrayContent(csvBytes);
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

        using var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"{ImportTasksPath}/{created.Id:D}/execute");
        executeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var executeResponse = await client.SendAsync(executeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, executeResponse.StatusCode, await executeResponse.Content.ReadAsStringAsync(cancellationToken));
        var executed = await executeResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>(cancellationToken);
        Assert.IsNotNull(executed);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, executed!.StatusKey);
        Assert.IsTrue(executed.SucceededRowCount >= 1);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}?page=1&pageSize=50");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(cancellationToken));
        var found = listJson.RootElement.GetProperty("items")
            .EnumerateArray()
            .Any(item => string.Equals(
                item.GetProperty("requestNumber").GetString(),
                requestNumber,
                StringComparison.Ordinal));
        Assert.IsTrue(found, "Imported enterprise request should appear in tenant list.");
    }

    private sealed class SubmissionEnvelope
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public byte[] Payload { get; set; } = [];
    }

    private static async Task<Guid> CreateOrganizationUnitAsync(
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

    private static async Task<Guid> GetCurrentUserIdAsync(
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

    private static async Task<string> LoginAndEnterAcmeTenantAsync(
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
