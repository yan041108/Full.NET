using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>验收工作流实例启动时的版本绑定和本人待办资源边界。</summary>
internal static class WorkflowRuntimeApiAssertions
{
    public static async Task VerifyStartAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await VerifyOpenApiAsync(client, cancellationToken);
        var identity = await factory.CreateHostIdentityAsync(
            $"workflow-runtime-{Guid.NewGuid():N}",
            [
                WorkflowPermissions.FormsCreate,
                WorkflowPermissions.FormsPublish,
                WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesStart,
                WorkflowPermissions.InstancesRead,
                WorkflowPermissions.InstancesCancel,
                WorkflowPermissions.TodosRead,
                WorkflowPermissions.TodosApprove,
                WorkflowPermissions.TodosReject,
            ],
            cancellationToken);
        var versions = await PublishRuntimeAssetsAsync(client, identity.AccessToken, cancellationToken);
        var businessId = Guid.NewGuid().ToString("N");

        using var invalidInitialValue = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", identity.AccessToken, new
            {
                definitionVersionId = versions.DefinitionVersionId,
                businessType = "leave.request",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = 42 },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidInitialValue.StatusCode);

        var startIdempotencyKey = $"start-{Guid.NewGuid():N}";
        using var start = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", identity.AccessToken, new
            {
                definitionVersionId = versions.DefinitionVersionId,
                businessType = "leave.request",
                businessId,
                initialValues = new { reason = "annual leave", secret = "classified" },
                idempotencyKey = startIdempotencyKey,
            }),
            cancellationToken);

        Assert.AreEqual(HttpStatusCode.Created, start.StatusCode, await start.Content.ReadAsStringAsync(cancellationToken));
        using var started = JsonDocument.Parse(await start.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(versions.DefinitionVersionId, started.RootElement.GetProperty("definitionVersionId").GetGuid());
        Assert.AreEqual(versions.FormVersionId, started.RootElement.GetProperty("formVersionId").GetGuid());
        Assert.AreEqual("active", started.RootElement.GetProperty("statusKey").GetString());
        var todoId = started.RootElement.GetProperty("activeTodoId").GetGuid();

        using var startReplay = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", identity.AccessToken, new
            {
                definitionVersionId = versions.DefinitionVersionId,
                businessType = "leave.request",
                businessId,
                initialValues = new { reason = "annual leave", secret = "classified" },
                idempotencyKey = startIdempotencyKey,
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, startReplay.StatusCode,
            await startReplay.Content.ReadAsStringAsync(cancellationToken));
        using var replayedStart = JsonDocument.Parse(await startReplay.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(started.RootElement.GetProperty("id").GetGuid(),
            replayedStart.RootElement.GetProperty("id").GetGuid());

        using var activeConflict = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", identity.AccessToken, new
            {
                definitionVersionId = versions.DefinitionVersionId,
                businessType = "leave.request",
                businessId,
                initialValues = new { reason = "annual leave" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, activeConflict.StatusCode);

        using var mine = await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/workflow/todos/mine", identity.AccessToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode, await mine.Content.ReadAsStringAsync(cancellationToken));
        using var todos = JsonDocument.Parse(await mine.Content.ReadAsStringAsync(cancellationToken));
        var todo = todos.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == todoId);
        Assert.AreEqual(identity.UserId, todo.GetProperty("assigneeUserId").GetGuid());
        Assert.AreEqual("active", todo.GetProperty("statusKey").GetString());

        using var todoDetail = await client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/workflow/todos/{todoId:D}/runtime", identity.AccessToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, todoDetail.StatusCode,
            await todoDetail.Content.ReadAsStringAsync(cancellationToken));
        using var detail = JsonDocument.Parse(await todoDetail.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(versions.FormVersionId, detail.RootElement.GetProperty("formVersionId").GetGuid());
        var visibleSchemaJson = detail.RootElement.GetProperty("formSchema").GetRawText();
        var expectedSchemaHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(visibleSchemaJson)));
        Assert.AreEqual(expectedSchemaHash, detail.RootElement.GetProperty("formSchemaHash").GetString());
        Assert.AreEqual("annual leave",
            detail.RootElement.GetProperty("submission").GetProperty("reason").GetString());
        Assert.IsFalse(detail.RootElement.GetProperty("submission").TryGetProperty("secret", out _));
        var detailFields = detail.RootElement.GetProperty("formSchema").GetProperty("sections")[0]
            .GetProperty("fields").EnumerateArray()
            .Select(field => field.GetProperty("fieldKey").GetString()).ToArray();
        CollectionAssert.AreEquivalent(new[] { "reason", "decision" }, detailFields);
        Assert.AreEqual("readOnly",
            detail.RootElement.GetProperty("fieldPolicies").GetProperty("reason").GetString());
        Assert.AreEqual("required",
            detail.RootElement.GetProperty("fieldPolicies").GetProperty("decision").GetString());

        var other = await factory.CreateHostIdentityAsync(
            $"workflow-other-{Guid.NewGuid():N}",
            [WorkflowPermissions.TodosApprove, WorkflowPermissions.InstancesRead],
            cancellationToken);
        using var forbidden = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", other.AccessToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new Dictionary<string, object?>(),
                    comment = "not mine",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var invalidTypePatch = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { reason = 42 },
                    comment = "invalid type",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, invalidTypePatch.StatusCode);

        using var hiddenPatch = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { secret = "exposed" },
                    comment = "hidden patch",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, hiddenPatch.StatusCode);

        using var missingRequiredPatch = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new Dictionary<string, object?>(),
                    comment = "missing required decision",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, missingRequiredPatch.StatusCode);

        using var invalidPatch = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { injected = "forbidden" },
                    comment = "invalid patch",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, invalidPatch.StatusCode);

        var approveIdempotencyKey = $"approve-{Guid.NewGuid():N}";
        using var approve = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "approved" },
                    comment = "approved",
                    idempotencyKey = approveIdempotencyKey,
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, approve.StatusCode, await approve.Content.ReadAsStringAsync(cancellationToken));
        using var approved = JsonDocument.Parse(await approve.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("completed", approved.RootElement.GetProperty("statusKey").GetString());
        Assert.AreEqual(2, approved.RootElement.GetProperty("revision").GetInt64());

        using var replay = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "approved" },
                    comment = "approved",
                    idempotencyKey = approveIdempotencyKey,
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode, await replay.Content.ReadAsStringAsync(cancellationToken));
        using var replayed = JsonDocument.Parse(await replay.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(approved.RootElement.GetProperty("revision").GetInt64(),
            replayed.RootElement.GetProperty("revision").GetInt64());

        var instanceId = started.RootElement.GetProperty("id").GetGuid();
        using var readInstance = await client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/workflow/instances/{instanceId:D}", identity.AccessToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, readInstance.StatusCode);
        using var instance = JsonDocument.Parse(await readInstance.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("completed", instance.RootElement.GetProperty("statusKey").GetString());

        using var forbiddenInstance = await client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/workflow/instances/{instanceId:D}", other.AccessToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenInstance.StatusCode);

        using var emptyMine = await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/workflow/todos/mine", identity.AccessToken),
            cancellationToken);
        using var remaining = JsonDocument.Parse(await emptyMine.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(0, remaining.RootElement.GetArrayLength());

        using var reopen = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", identity.AccessToken, new
            {
                definitionVersionId = versions.DefinitionVersionId,
                businessType = "leave.request",
                businessId,
                initialValues = new { reason = "reopened request" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, reopen.StatusCode, await reopen.Content.ReadAsStringAsync(cancellationToken));
        using var reopened = JsonDocument.Parse(await reopen.Content.ReadAsStringAsync(cancellationToken));
        var reopenedTodoId = reopened.RootElement.GetProperty("activeTodoId").GetGuid();

        using var reject = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{reopenedTodoId:D}/reject", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "rejected" },
                    comment = "rejected",
                    idempotencyKey = $"reject-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, reject.StatusCode, await reject.Content.ReadAsStringAsync(cancellationToken));
        using var rejected = JsonDocument.Parse(await reject.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("rejected", rejected.RootElement.GetProperty("statusKey").GetString());

        var reopenedInstanceId = reopened.RootElement.GetProperty("id").GetGuid();
        using var logs = await client.SendAsync(
            Authorized(HttpMethod.Get,
                $"/api/v1/workflow/instances/{reopenedInstanceId:D}/execution-logs",
                identity.AccessToken), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, logs.StatusCode,
            await logs.Content.ReadAsStringAsync(cancellationToken));
        using var executionLogs = JsonDocument.Parse(await logs.Content.ReadAsStringAsync(cancellationToken));
        CollectionAssert.AreEqual(
            new[] { "instance.start", "todo.reject" },
            executionLogs.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("transitionKey").GetString()).ToArray());

        var cancelBusinessId = Guid.NewGuid().ToString("N");
        using var cancelStart = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", identity.AccessToken, new
            {
                definitionVersionId = versions.DefinitionVersionId,
                businessType = "leave.request",
                businessId = cancelBusinessId,
                initialValues = new { reason = "cancelled request" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, cancelStart.StatusCode,
            await cancelStart.Content.ReadAsStringAsync(cancellationToken));
        using var cancelStarted = JsonDocument.Parse(
            await cancelStart.Content.ReadAsStringAsync(cancellationToken));
        var cancelInstanceId = cancelStarted.RootElement.GetProperty("id").GetGuid();

        using var permissionDeniedCancel = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post,
                $"/api/v1/workflow/instances/{cancelInstanceId:D}/cancel", other.AccessToken,
                new
                {
                    expectedRevision = 1,
                    reason = "missing action permission",
                    idempotencyKey = $"cancel-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, permissionDeniedCancel.StatusCode);

        var unrelatedCanceller = await factory.CreateHostIdentityAsync(
            $"workflow-canceller-{Guid.NewGuid():N}",
            [WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesCancel],
            cancellationToken);
        using var resourceDeniedCancel = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post,
                $"/api/v1/workflow/instances/{cancelInstanceId:D}/cancel", unrelatedCanceller.AccessToken,
                new
                {
                    expectedRevision = 1,
                    reason = "not a participant",
                    idempotencyKey = $"cancel-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, resourceDeniedCancel.StatusCode);

        var cancelIdempotencyKey = $"cancel-{Guid.NewGuid():N}";
        using var cancel = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post,
                $"/api/v1/workflow/instances/{cancelInstanceId:D}/cancel", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    reason = "request withdrawn",
                    idempotencyKey = cancelIdempotencyKey,
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode,
            await cancel.Content.ReadAsStringAsync(cancellationToken));
        using var cancelled = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("cancelled", cancelled.RootElement.GetProperty("statusKey").GetString());
        Assert.AreEqual(2, cancelled.RootElement.GetProperty("revision").GetInt64());
        Assert.AreEqual(JsonValueKind.Null,
            cancelled.RootElement.GetProperty("activeTodoId").ValueKind);

        using var cancelReplay = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post,
                $"/api/v1/workflow/instances/{cancelInstanceId:D}/cancel", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    reason = "request withdrawn",
                    idempotencyKey = cancelIdempotencyKey,
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, cancelReplay.StatusCode,
            await cancelReplay.Content.ReadAsStringAsync(cancellationToken));

        using var changedReplay = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post,
                $"/api/v1/workflow/instances/{cancelInstanceId:D}/cancel", identity.AccessToken,
                new
                {
                    expectedRevision = 1,
                    reason = "different request",
                    idempotencyKey = cancelIdempotencyKey,
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, changedReplay.StatusCode);

        using var terminalCancel = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post,
                $"/api/v1/workflow/instances/{cancelInstanceId:D}/cancel", identity.AccessToken,
                new
                {
                    expectedRevision = 2,
                    reason = "cancel twice",
                    idempotencyKey = $"cancel-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, terminalCancel.StatusCode);

        using var cancelLogs = await client.SendAsync(
            Authorized(HttpMethod.Get,
                $"/api/v1/workflow/instances/{cancelInstanceId:D}/execution-logs",
                identity.AccessToken), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, cancelLogs.StatusCode);
        using var cancellationLogs = JsonDocument.Parse(
            await cancelLogs.Content.ReadAsStringAsync(cancellationToken));
        CollectionAssert.AreEqual(
            new[] { "instance.start", "instance.cancel" },
            cancellationLogs.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("transitionKey").GetString()).ToArray());

        var concurrentBusinessId = Guid.NewGuid().ToString("N");
        using var concurrentStart = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", identity.AccessToken, new
            {
                definitionVersionId = versions.DefinitionVersionId,
                businessType = "leave.request",
                businessId = concurrentBusinessId,
                initialValues = new { reason = "concurrent decision" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, concurrentStart.StatusCode);
        using var concurrentInstance = JsonDocument.Parse(
            await concurrentStart.Content.ReadAsStringAsync(cancellationToken));
        var concurrentInstanceId = concurrentInstance.RootElement.GetProperty("id").GetGuid();
        var concurrentTodoId = concurrentInstance.RootElement.GetProperty("activeTodoId").GetGuid();
        using var approveRequest = AuthorizedJson(
            HttpMethod.Post, $"/api/v1/workflow/todos/{concurrentTodoId:D}/approve", identity.AccessToken,
            new
            {
                expectedRevision = 1,
                fieldPatch = new { decision = "approved" },
                comment = "approve race",
                idempotencyKey = $"approve-{Guid.NewGuid():N}",
            });
        using var rejectRequest = AuthorizedJson(
            HttpMethod.Post, $"/api/v1/workflow/todos/{concurrentTodoId:D}/reject", identity.AccessToken,
            new
            {
                expectedRevision = 1,
                fieldPatch = new { decision = "rejected" },
                comment = "reject race",
                idempotencyKey = $"reject-{Guid.NewGuid():N}",
            });
        var decisions = await Task.WhenAll(
            client.SendAsync(approveRequest, cancellationToken),
            client.SendAsync(rejectRequest, cancellationToken));
        using var firstDecision = decisions[0];
        using var secondDecision = decisions[1];
        CollectionAssert.AreEquivalent(
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            decisions.Select(response => response.StatusCode).ToArray());

        using var concurrentRead = await client.SendAsync(
            Authorized(HttpMethod.Get,
                $"/api/v1/workflow/instances/{concurrentInstanceId:D}", identity.AccessToken),
            cancellationToken);
        using var concurrentResult = JsonDocument.Parse(
            await concurrentRead.Content.ReadAsStringAsync(cancellationToken));
        CollectionAssert.Contains(
            new[] { "completed", "rejected" },
            concurrentResult.RootElement.GetProperty("statusKey").GetString());
    }

    private static async Task<PublishedRuntimeAssets> PublishRuntimeAssetsAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var createForm = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/forms", token, new
            {
                formKey = $"runtime.{Guid.NewGuid():N}",
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
                                new
                                {
                                    fieldKey = "secret",
                                    fieldTypeKey = "text",
                                    required = false,
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
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createForm.StatusCode);
        using var form = JsonDocument.Parse(await createForm.Content.ReadAsStringAsync(cancellationToken));
        var formId = form.RootElement.GetProperty("id").GetGuid();

        using var publishForm = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/forms/{formId:D}/publish", token,
                new { expectedRevision = 1 }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publishForm.StatusCode);
        using var formVersion = JsonDocument.Parse(await publishForm.Content.ReadAsStringAsync(cancellationToken));
        var formVersionId = formVersion.RootElement.GetProperty("id").GetGuid();

        using var createDefinition = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/definitions", token, new
            {
                definitionKey = $"runtime.{Guid.NewGuid():N}",
                draft = new
                {
                    schemaVersion = 1,
                    nodes = new object[]
                    {
                        new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "approve" } } },
                        new
                        {
                            nodeKey = "approve",
                            nodeTypeKey = "human.approval",
                            nodeSchemaVersion = 1,
                            config = new
                            {
                                nextNodeKeys = new[] { "end" },
                                fieldPolicies = new Dictionary<string, string>
                                {
                                    ["reason"] = "readOnly",
                                    ["secret"] = "hidden",
                                    ["decision"] = "required",
                                },
                            },
                        },
                        new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { nextNodeKeys = Array.Empty<string>() } },
                    },
                },
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createDefinition.StatusCode);
        using var definition = JsonDocument.Parse(await createDefinition.Content.ReadAsStringAsync(cancellationToken));
        var definitionId = definition.RootElement.GetProperty("id").GetGuid();

        using var publishDefinition = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/definitions/{definitionId:D}/publish", token,
                new { expectedRevision = 1, formVersionId }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publishDefinition.StatusCode);
        using var definitionVersion = JsonDocument.Parse(await publishDefinition.Content.ReadAsStringAsync(cancellationToken));
        return new(definitionVersion.RootElement.GetProperty("id").GetGuid(), formVersionId);
    }

    private static async Task VerifyOpenApiAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/instances", HttpMethod.Post,
            "workflowStartInstance", "WorkflowInstances", 201, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/instances/{instanceId}/cancel", HttpMethod.Post,
            "workflowCancelInstance", "WorkflowInstances", 200, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/todos/{todoId}", HttpMethod.Get,
            "workflowGetTodo", "WorkflowTodos", 200, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/todos/{todoId}/approve", HttpMethod.Post,
            "workflowApproveTodo", "WorkflowTodos", 200, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/todos/{todoId}/reject", HttpMethod.Post,
            "workflowRejectTodo", "WorkflowTodos", 200, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/instances/{instanceId}/execution-logs", HttpMethod.Get,
            "workflowListInstanceExecutionLogs", "WorkflowInstances", 200, "application/json");
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

    private sealed record PublishedRuntimeAssets(Guid DefinitionVersionId, Guid FormVersionId);
}
