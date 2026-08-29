using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>验收工作流草稿、发布、权限和不可变表单版本绑定。</summary>
internal static class WorkflowApiAssertions
{
    private const string FormsPath = "/api/v1/workflow/forms";
    private const string DefinitionsPath = "/api/v1/workflow/definitions";

    public static async Task VerifyAsync(FullNetApiFactory factory, CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        using (var anonymous = await client.GetAsync(DefinitionsPath, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var wrongToken = await factory.CreateHostAccessTokenAsync(["platform.dashboard.read"], cancellationToken);
        using (var forbidden = await client.SendAsync(Authorized(HttpMethod.Get, DefinitionsPath, wrongToken), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        await VerifyOpenApiAsync(client, cancellationToken);

        var identity = await factory.CreateHostIdentityAsync(
            $"workflow-writer-{Guid.NewGuid():N}",
            [
                WorkflowPermissions.FormsRead,
                WorkflowPermissions.FormsCreate,
                WorkflowPermissions.FormsUpdate,
                WorkflowPermissions.FormsPublish,
                WorkflowPermissions.DefinitionsRead,
                WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsUpdate,
                WorkflowPermissions.DefinitionsPublish,
            ],
            cancellationToken);

        var formVersionId = await CreateAndPublishFormAsync(client, identity.AccessToken, cancellationToken);
        var definitionVersion = await CreateAndPublishDefinitionAsync(
            client, identity.AccessToken, formVersionId, cancellationToken);

        Assert.AreEqual(formVersionId, definitionVersion.GetProperty("formVersionId").GetGuid());
        var versionId = definitionVersion.GetProperty("id").GetGuid();
        using var read = await client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/workflow/definition-versions/{versionId:D}", identity.AccessToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(formVersionId, readJson.RootElement.GetProperty("formVersionId").GetGuid());
    }

    private static async Task VerifyOpenApiAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, FormsPath, HttpMethod.Post,
            "workflowCreateForm", "WorkflowForms", 201, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, FormsPath + "/{formId}/publish", HttpMethod.Post,
            "workflowPublishForm", "WorkflowForms", 200, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, DefinitionsPath + "/{definitionId}/publish", HttpMethod.Post,
            "workflowPublishDefinition", "WorkflowDefinitions", 200, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/definition-versions/{versionId}", HttpMethod.Get,
            "workflowGetDefinitionVersion", "WorkflowDefinitions", 200, "application/json");
    }

    private static async Task<Guid> CreateAndPublishFormAsync(
        HttpClient client, string token, CancellationToken cancellationToken)
    {
        using var create = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, FormsPath, token, new
            {
                formKey = $"leave.{Guid.NewGuid():N}",
                draft = new
                {
                    schemaVersion = 1,
                    adapterVersion = 1,
                    sections = new[]
                    {
                        new
                        {
                            sectionKey = "main",
                            fields = new[]
                            {
                                new
                                {
                                    fieldKey = "reason",
                                    fieldTypeKey = "text",
                                    required = true,
                                    constraints = new Dictionary<string, object?>(),
                                },
                            },
                        },
                    },
                },
            }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync(cancellationToken));
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync(cancellationToken));
        var formId = created.RootElement.GetProperty("id").GetGuid();

        using var publish = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"{FormsPath}/{formId:D}/publish", token, new { expectedRevision = 1 }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publish.StatusCode, await publish.Content.ReadAsStringAsync(cancellationToken));
        using var published = JsonDocument.Parse(await publish.Content.ReadAsStringAsync(cancellationToken));
        return published.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> CreateAndPublishDefinitionAsync(
        HttpClient client, string token, Guid formVersionId, CancellationToken cancellationToken)
    {
        using var create = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, DefinitionsPath, token, new
            {
                definitionKey = $"leave.{Guid.NewGuid():N}",
                draft = CreateDefinitionDraft("v1"),
            }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync(cancellationToken));
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync(cancellationToken));
        var definitionId = created.RootElement.GetProperty("id").GetGuid();

        using var publish = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"{DefinitionsPath}/{definitionId:D}/publish", token,
                new { expectedRevision = 1, formVersionId }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publish.StatusCode, await publish.Content.ReadAsStringAsync(cancellationToken));
        using var published = JsonDocument.Parse(await publish.Content.ReadAsStringAsync(cancellationToken));
        var firstVersion = published.RootElement.Clone();

        using var stale = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"{DefinitionsPath}/{definitionId:D}/publish", token,
                new { expectedRevision = 1, formVersionId }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);

        using var update = await client.SendAsync(
            AuthorizedJson(HttpMethod.Put, $"{DefinitionsPath}/{definitionId:D}/draft", token,
                new { expectedRevision = 2, draft = CreateDefinitionDraft("v2") }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, update.StatusCode, await update.Content.ReadAsStringAsync(cancellationToken));

        using var secondPublish = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"{DefinitionsPath}/{definitionId:D}/publish", token,
                new { expectedRevision = 3, formVersionId }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondPublish.StatusCode, await secondPublish.Content.ReadAsStringAsync(cancellationToken));
        using var secondPublished = JsonDocument.Parse(await secondPublish.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(2, secondPublished.RootElement.GetProperty("versionNumber").GetInt32());

        var firstVersionId = firstVersion.GetProperty("id").GetGuid();
        using var firstRead = await client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/workflow/definition-versions/{firstVersionId:D}", token),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstRead.StatusCode);
        using var firstReadJson = JsonDocument.Parse(await firstRead.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(1, firstReadJson.RootElement.GetProperty("versionNumber").GetInt32());
        Assert.AreEqual(firstVersion.GetProperty("contentHash").GetString(), firstReadJson.RootElement.GetProperty("contentHash").GetString());
        return secondPublished.RootElement.Clone();
    }

    private static object CreateDefinitionDraft(string label) => new
    {
        schemaVersion = 1,
        nodes = new object[]
        {
            new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { label, nextNodeKeys = new[] { "end" } } },
            new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { nextNodeKeys = Array.Empty<string>() } },
        },
    };

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(HttpMethod method, string path, string token, T body)
    {
        var request = Authorized(method, path, token);
        request.Content = JsonContent.Create(body);
        return request;
    }
}
