using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Modules.EnterpriseRequest.Contracts;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static class EnterpriseRequestWorkflowBootstrap
{
    private const string FormsPath = "/api/v1/workflow/forms";
    private const string DefinitionsPath = "/api/v1/workflow/definitions";

    public static async Task PublishDemoApprovalDefinitionInTenantAsync(
        HttpClient client,
        string tenantToken,
        CancellationToken cancellationToken = default)
    {
        var formVersionId = await CreateAndPublishFormAsync(client, tenantToken, cancellationToken);
        await CreateAndPublishDefinitionAsync(
            client,
            tenantToken,
            formVersionId,
            EnterpriseRequestWorkflowConstants.DefinitionKey,
            cancellationToken);
    }

    private static async Task<Guid> CreateAndPublishFormAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var create = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, FormsPath, token, new
            {
                formKey = "demo.enterprise_request." + Guid.NewGuid().ToString("N"),
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
                                    fieldKey = "title",
                                    fieldTypeKey = "text",
                                    required = false,
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
            AuthorizedJson(HttpMethod.Post, FormsPath + "/" + formId.ToString("D") + "/publish", token, new { expectedRevision = 1 }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publish.StatusCode, await publish.Content.ReadAsStringAsync(cancellationToken));
        using var published = JsonDocument.Parse(await publish.Content.ReadAsStringAsync(cancellationToken));
        return published.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task CreateAndPublishDefinitionAsync(
        HttpClient client,
        string token,
        Guid formVersionId,
        string definitionKey,
        CancellationToken cancellationToken)
    {
        using var create = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, DefinitionsPath, token, new
            {
                definitionKey,
                draft = new
                {
                    schemaVersion = 1,
                    nodes = new object[]
                    {
                        new
                        {
                            nodeKey = "start",
                            nodeTypeKey = "start",
                            nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "approve" } },
                        },
                        new
                        {
                            nodeKey = "approve",
                            nodeTypeKey = "human.approval",
                            nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "end" } },
                        },
                        new
                        {
                            nodeKey = "end",
                            nodeTypeKey = "end",
                            nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = Array.Empty<string>() },
                        },
                    },
                },
            }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync(cancellationToken));
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync(cancellationToken));
        var definitionId = created.RootElement.GetProperty("id").GetGuid();

        using var publish = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, DefinitionsPath + "/" + definitionId.ToString("D") + "/publish", token,
                new { expectedRevision = 1, formVersionId }),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publish.StatusCode, await publish.Content.ReadAsStringAsync(cancellationToken));
    }

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