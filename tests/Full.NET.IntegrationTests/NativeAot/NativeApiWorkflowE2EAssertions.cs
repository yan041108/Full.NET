using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// 验证 Workflow HTTP、JSON 与 Dapper 路径可由 Linux Native Host.Api 完整执行。
/// </summary>
internal static class NativeApiWorkflowE2EAssertions
{
    public static async Task VerifyWorkflowFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);

        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            provider,
            connectionString,
            new Dictionary<string, string?>(),
            TimeSpan.FromMinutes(3),
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(
                client,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var assets = await PublishAssetsAsync(client, token, cancellationToken)
            .ConfigureAwait(false);

        await VerifyLinearApprovalAsync(client, token, assets, cancellationToken)
            .ConfigureAwait(false);
        await VerifyTerminalRejectionAsync(client, token, assets, cancellationToken)
            .ConfigureAwait(false);

        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    private static async Task VerifyLinearApprovalAsync(
        HttpClient client,
        string token,
        PublishedWorkflowAssets assets,
        CancellationToken cancellationToken)
    {
        using var startResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", token, new
            {
                definitionVersionId = assets.DefinitionVersionId,
                businessType = "native.workflow.approval",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "native approval" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
                startResponse,
                HttpStatusCode.Created,
                "Start Workflow instance in Native Host.Api",
                cancellationToken)
            .ConfigureAwait(false);
        using var started = JsonDocument.Parse(
            await startResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var firstTodoId = started.RootElement.GetProperty("activeTodoId").GetGuid();

        using var firstApproveResponse = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"/api/v1/workflow/todos/{firstTodoId:D}/approve",
                token,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "first-approved" },
                    comment = "Native first stage",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
                firstApproveResponse,
                HttpStatusCode.OK,
                "Advance Workflow to second approval in Native Host.Api",
                cancellationToken)
            .ConfigureAwait(false);
        using var advanced = JsonDocument.Parse(
            await firstApproveResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Assert.AreEqual("active", advanced.RootElement.GetProperty("statusKey").GetString());
        var secondTodoId = advanced.RootElement.GetProperty("activeTodoId").GetGuid();
        Assert.AreNotEqual(firstTodoId, secondTodoId);

        using var secondApproveResponse = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"/api/v1/workflow/todos/{secondTodoId:D}/approve",
                token,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "final-approved" },
                    comment = "Native final stage",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
                secondApproveResponse,
                HttpStatusCode.OK,
                "Complete Workflow in Native Host.Api",
                cancellationToken)
            .ConfigureAwait(false);
        using var completed = JsonDocument.Parse(
            await secondApproveResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Assert.AreEqual("completed", completed.RootElement.GetProperty("statusKey").GetString());
        Assert.AreEqual(JsonValueKind.Null, completed.RootElement.GetProperty("activeTodoId").ValueKind);
    }

    private static async Task VerifyTerminalRejectionAsync(
        HttpClient client,
        string token,
        PublishedWorkflowAssets assets,
        CancellationToken cancellationToken)
    {
        using var startResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", token, new
            {
                definitionVersionId = assets.DefinitionVersionId,
                businessType = "native.workflow.rejection",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "native rejection" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
                startResponse,
                HttpStatusCode.Created,
                "Start rejectable Workflow in Native Host.Api",
                cancellationToken)
            .ConfigureAwait(false);
        using var started = JsonDocument.Parse(
            await startResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var todoId = started.RootElement.GetProperty("activeTodoId").GetGuid();

        using var rejectResponse = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"/api/v1/workflow/todos/{todoId:D}/reject",
                token,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "rejected" },
                    comment = "Native terminal rejection",
                    idempotencyKey = $"reject-{Guid.NewGuid():N}",
                }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
                rejectResponse,
                HttpStatusCode.OK,
                "Reject Workflow in Native Host.Api",
                cancellationToken)
            .ConfigureAwait(false);
        using var rejected = JsonDocument.Parse(
            await rejectResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Assert.AreEqual("rejected", rejected.RootElement.GetProperty("statusKey").GetString());
        Assert.AreEqual(JsonValueKind.Null, rejected.RootElement.GetProperty("activeTodoId").ValueKind);
    }

    private static async Task<PublishedWorkflowAssets> PublishAssetsAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var createFormResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/forms", token, new
            {
                formKey = $"native.workflow.{Guid.NewGuid():N}",
                draft = new
                {
                    schemaVersion = 1,
                    adapterVersion = 1,
                    sections = new[]
                    {
                        new
                        {
                            sectionKey = "main",
                            fields = new object[]
                            {
                                new
                                {
                                    fieldKey = "reason",
                                    fieldTypeKey = "text",
                                    required = true,
                                    constraints = new Dictionary<string, object?>(),
                                },
                                new
                                {
                                    fieldKey = "decision",
                                    fieldTypeKey = "text",
                                    required = false,
                                    constraints = new Dictionary<string, object?>(),
                                },
                            },
                        },
                    },
                },
            }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
                createFormResponse,
                HttpStatusCode.Created,
                "Create Workflow form in Native Host.Api",
                cancellationToken)
            .ConfigureAwait(false);
        using var form = JsonDocument.Parse(
            await createFormResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var formId = form.RootElement.GetProperty("id").GetGuid();

        using var publishFormResponse = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"/api/v1/workflow/forms/{formId:D}/publish",
                token,
                new { expectedRevision = 1 }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
                publishFormResponse,
                HttpStatusCode.OK,
                "Publish Workflow form in Native Host.Api",
                cancellationToken)
            .ConfigureAwait(false);
        using var formVersion = JsonDocument.Parse(
            await publishFormResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var formVersionId = formVersion.RootElement.GetProperty("id").GetGuid();

        var fieldPolicies = new Dictionary<string, string>
        {
            ["reason"] = "readOnly",
            ["decision"] = "required",
        };
        using var createDefinitionResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/definitions", token, new
            {
                definitionKey = $"native.workflow.{Guid.NewGuid():N}",
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
                            config = new { nextNodeKeys = new[] { "first" } },
                        },
                        new
                        {
                            nodeKey = "first",
                            nodeTypeKey = "human.approval",
                            nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "second" }, fieldPolicies },
                        },
                        new
                        {
                            nodeKey = "second",
                            nodeTypeKey = "human.approval",
                            nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "end" }, fieldPolicies },
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
            }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
                createDefinitionResponse,
                HttpStatusCode.Created,
                "Create Workflow definition in Native Host.Api",
                cancellationToken)
            .ConfigureAwait(false);
        using var definition = JsonDocument.Parse(
            await createDefinitionResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var definitionId = definition.RootElement.GetProperty("id").GetGuid();

        using var publishDefinitionResponse = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"/api/v1/workflow/definitions/{definitionId:D}/publish",
                token,
                new { expectedRevision = 1, formVersionId }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
                publishDefinitionResponse,
                HttpStatusCode.OK,
                "Publish Workflow definition in Native Host.Api",
                cancellationToken)
            .ConfigureAwait(false);
        using var definitionVersion = JsonDocument.Parse(
            await publishDefinitionResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return new PublishedWorkflowAssets(
            definitionVersion.RootElement.GetProperty("id").GetGuid(),
            formVersionId);
    }

    private static HttpRequestMessage AuthorizedJson<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Origin", "http://localhost");
        return request;
    }

    private sealed record PublishedWorkflowAssets(
        Guid DefinitionVersionId,
        Guid FormVersionId);
}
