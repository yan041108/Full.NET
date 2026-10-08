using System.IO.Compression;
using System.Xml.Linq;
using Full.NET.Abstractions.Tenancy;
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

internal static partial class EnterpriseRequestAssertions
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
        await VerifyApprovalProgressReadBoundaryAsync(factory, client, token, created, cancellationToken);
        await VerifyLinesAsync(factory, client, token, created, cancellationToken);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{created.Id:D}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);

        using var submitRequest = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{created.Id:D}/submit-for-approval");
        submitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var submitResponse = await client.SendAsync(submitRequest, cancellationToken);
        Assert.IsTrue(
            submitResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
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
            applicantUserId = Guid.NewGuid(),
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
        await VerifySubmissionRollbackAsync(factory, created, cancellationToken);
        submitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var submitResponse = await client.SendAsync(submitRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, submitResponse.StatusCode, await submitResponse.Content.ReadAsStringAsync(cancellationToken));
        var submitted = await submitResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(cancellationToken);
        Assert.IsNotNull(submitted);
        Assert.AreEqual(EnterpriseRequestStatusKeys.Submitted, submitted!.Status);
        await VerifyReliableApprovalRecoveryAsync(factory, client, token, submitted, cancellationToken);
    }

    public static async Task VerifyTenantDemoEnterpriseRequestsWorkbookImportAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default,
        Func<ImportExportTaskDetailResponse, string, Task<ImportExportTaskDetailResponse>>? executeQueued = null)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var organizationUnitId = await CreateOrganizationUnitAsync(client, token, cancellationToken);
        var applicantUserId = Guid.NewGuid();
        var requestNumber = $"IMP-{Guid.NewGuid():N}".Substring(0, 16);
        using var templateRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/import-export/schemas/{StaticImportSchemaKeys.DemoEnterpriseRequests}/worksheets/requests/template");
        templateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var templateResponse = await client.SendAsync(templateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, templateResponse.StatusCode);
        var workbookBytes = await templateResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        using (var workbook = new MemoryStream())
        {
            workbook.Write(workbookBytes);
            using (var archive = new ZipArchive(workbook, ZipArchiveMode.Update, leaveOpen: true))
            {
                var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
                XDocument document; using (var input = entry.Open()) document = XDocument.Load(input);
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var values = new[] { requestNumber, "Imported row, with comma", "12.5", applicantUserId.ToString("D"), organizationUnitId.ToString("D") };
                document.Root!.Element(ns + "sheetData")!.Add(new XElement(ns + "row", new XAttribute("r", 2),
                    values.Select((value, index) => new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + index)}2"),
                        new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value))))));
                entry.Delete(); using var output = archive.CreateEntry("xl/worksheets/sheet1.xml").Open(); document.Save(output);
            }
            workbookBytes = workbook.ToArray();
        }

        using var createContent = new MultipartFormDataContent
        {
            { new StringContent(StaticImportSchemaKeys.DemoEnterpriseRequests), "schemaKey" },
            { new StringContent("requests"), "worksheetKey" },
        };
        var fileContent = new ByteArrayContent(workbookBytes);
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
        Assert.AreEqual(StaticImportSchemaKeys.DemoEnterpriseRequests, created.SchemaKey);
        Assert.AreEqual(1, created.TotalRows); Assert.AreEqual(1, created.ValidRowCount); Assert.AreEqual(0, created.InvalidRowCount);
        Assert.AreEqual(2, created.PreviewRows.Single().LineNumber);

        using var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"{ImportTasksPath}/{created.Id:D}/execute");
        executeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var executeResponse = await client.SendAsync(executeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, executeResponse.StatusCode, await executeResponse.Content.ReadAsStringAsync(cancellationToken));
        var executed = await executeResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>(cancellationToken);
        Assert.IsNotNull(executed);
        if (executeQueued is not null)
        {
            Assert.AreEqual(ImportExportTaskStatusKeys.Queued, executed!.StatusKey);
            executed = await executeQueued(executed, token);
        }
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, executed!.StatusKey);
        Assert.AreEqual(1, executed.SucceededRowCount); Assert.AreEqual(1, executed.NextLineNumber);
        // 模拟业务已提交、调度检查点尚未提交的恢复窗口，正式处理器重放必须返回同一个实体。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            tenant.SetTenant(new TenantContext(created.TenantId, "acme", "Acme"));
            try
            {
                var handler = scope.ServiceProvider.GetServices<IStaticImportSchemaHandler>().Single(item => item.SchemaKey == created.SchemaKey);
                var context = new StaticImportPreviewContext(created.RequestedByUserId, new Dictionary<string, bool>()) { TaskId = created.Id };
                using var replayContent = new MemoryStream(workbookBytes);
                var replay = await handler.ExecuteBatchAsync(replayContent, replayContent.Length, 0, 1, context, cancellationToken);
                Assert.IsTrue(replay.IsSuccess); Assert.IsTrue(replay.Value!.Rows.Single().Succeeded);
                using var secondContent = new MemoryStream(workbookBytes);
                var second = await handler.ExecuteBatchAsync(secondContent, secondContent.Length, 0, 1, context, cancellationToken);
                Assert.AreEqual(replay.Value.Rows.Single().EntityId, second.Value!.Rows.Single().EntityId);
            }
            finally { tenant.Clear(); }
        }

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}?page=1&pageSize=50");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(cancellationToken));
        var matching = listJson.RootElement.GetProperty("items")
            .EnumerateArray()
            .Count(item => string.Equals(
                item.GetProperty("requestNumber").GetString(),
                requestNumber,
                StringComparison.Ordinal));
        Assert.AreEqual(1, matching, "Recovered import must create exactly one enterprise request.");
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
