using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>验收工作流实例启动、表单安全 Patch、Host/Tenant 隔离和本人待办资源边界。</summary>
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
                WorkflowPermissions.CcRead,
                WorkflowPermissions.CcMarkRead,
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
            [
                WorkflowPermissions.TodosApprove,
                WorkflowPermissions.InstancesRead,
                WorkflowPermissions.CcRead,
                WorkflowPermissions.CcMarkRead,
            ],
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

        await AssertDangerousPatchesRejectedAsync(
            client, identity.AccessToken, todoId, cancellationToken);

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

        await VerifyLinearMultiApprovalAsync(
            client, identity.AccessToken, versions.FormVersionId, cancellationToken);
        await VerifyCcRuntimeAsync(
            client,
            identity.AccessToken,
            other.AccessToken,
            other.UserId,
            versions.FormVersionId,
            cancellationToken);

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

    /// <summary>
    /// 在种子租户内复验同意/拒绝、危险 Patch 422、旧修订 409、精确权限 403，以及禁止引用 Host 定义。
    /// </summary>
    public static async Task VerifyTenantScopeAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        // Host/Tenant 会话 Cookie 不能共用同一个 HttpClient，否则后一次上下文会覆盖先签发的 Bearer。
        using var hostClient = factory.CreateClientForHost("localhost");
        using var tenantClient = factory.CreateClientForHost("localhost");
        using var limitedClient = factory.CreateClientForHost("localhost");
        var hostAdminToken = await LoginAsHostAdminAsync(hostClient, cancellationToken);
        var tenantAdminToken = await EnterAcmeTenantAsync(
            tenantClient,
            await LoginAsHostAdminAsync(tenantClient, cancellationToken),
            cancellationToken);

        var hostVersions = await PublishRuntimeAssetsAsync(hostClient, hostAdminToken, cancellationToken);
        using var crossScopeStart = await tenantClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", tenantAdminToken, new
            {
                definitionVersionId = hostVersions.DefinitionVersionId,
                businessType = "leave.request",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "must not bind host version" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        var crossScopeBody = await crossScopeStart.Content.ReadAsStringAsync(cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, crossScopeStart.StatusCode, crossScopeBody);
        using var crossScopeProblem = JsonDocument.Parse(crossScopeBody);
        Assert.AreEqual(WorkflowErrorCodes.VersionNotPublished,
            crossScopeProblem.RootElement.GetProperty("code").GetString());

        var tenantVersions = await PublishRuntimeAssetsAsync(tenantClient, tenantAdminToken, cancellationToken);
        using var start = await tenantClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", tenantAdminToken, new
            {
                definitionVersionId = tenantVersions.DefinitionVersionId,
                businessType = "leave.request",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "tenant approved", secret = "classified" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, start.StatusCode,
            await start.Content.ReadAsStringAsync(cancellationToken));
        using var started = JsonDocument.Parse(await start.Content.ReadAsStringAsync(cancellationToken));
        var todoId = started.RootElement.GetProperty("activeTodoId").GetGuid();
        var instanceId = started.RootElement.GetProperty("id").GetGuid();

        using var hostRead = await hostClient.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/workflow/instances/{instanceId:D}", hostAdminToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, hostRead.StatusCode);

        await AssertDangerousPatchesRejectedAsync(tenantClient, tenantAdminToken, todoId, cancellationToken);

        using var approve = await tenantClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", tenantAdminToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "approved" },
                    comment = "tenant approved",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, approve.StatusCode,
            await approve.Content.ReadAsStringAsync(cancellationToken));
        using var approved = JsonDocument.Parse(await approve.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("completed", approved.RootElement.GetProperty("statusKey").GetString());

        using var rejectStart = await tenantClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", tenantAdminToken, new
            {
                definitionVersionId = tenantVersions.DefinitionVersionId,
                businessType = "leave.request",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "tenant rejected" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, rejectStart.StatusCode,
            await rejectStart.Content.ReadAsStringAsync(cancellationToken));
        using var rejectStarted = JsonDocument.Parse(
            await rejectStart.Content.ReadAsStringAsync(cancellationToken));
        var rejectTodoId = rejectStarted.RootElement.GetProperty("activeTodoId").GetGuid();
        using var reject = await tenantClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{rejectTodoId:D}/reject", tenantAdminToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "rejected" },
                    comment = "tenant rejected",
                    idempotencyKey = $"reject-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, reject.StatusCode,
            await reject.Content.ReadAsStringAsync(cancellationToken));
        using var rejected = JsonDocument.Parse(await reject.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("rejected", rejected.RootElement.GetProperty("statusKey").GetString());

        using var staleStart = await tenantClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", tenantAdminToken, new
            {
                definitionVersionId = tenantVersions.DefinitionVersionId,
                businessType = "leave.request",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "stale revision" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        using var staleStarted = JsonDocument.Parse(
            await staleStart.Content.ReadAsStringAsync(cancellationToken));
        var staleTodoId = staleStarted.RootElement.GetProperty("activeTodoId").GetGuid();
        using var firstApprove = await tenantClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{staleTodoId:D}/approve", tenantAdminToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "first" },
                    comment = "authoritative",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstApprove.StatusCode);
        using var staleApprove = await tenantClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{staleTodoId:D}/approve", tenantAdminToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "stale" },
                    comment = "stale",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleApprove.StatusCode);

        var limitedTenantToken = await EnterAcmeTenantWithRolePermissionsAsync(
            limitedClient,
            [
                WorkflowPermissions.FormsRead,
                WorkflowPermissions.FormsCreate,
                WorkflowPermissions.FormsPublish,
                WorkflowPermissions.DefinitionsRead,
                WorkflowPermissions.DefinitionsCreate,
                WorkflowPermissions.DefinitionsPublish,
                WorkflowPermissions.InstancesRead,
                WorkflowPermissions.InstancesStart,
                WorkflowPermissions.TodosRead,
            ],
            cancellationToken);
        var limitedVersions = await PublishRuntimeAssetsAsync(limitedClient, limitedTenantToken, cancellationToken);
        using var limitedStart = await limitedClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", limitedTenantToken, new
            {
                definitionVersionId = limitedVersions.DefinitionVersionId,
                businessType = "leave.request",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "limited tenant" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, limitedStart.StatusCode,
            await limitedStart.Content.ReadAsStringAsync(cancellationToken));
        using var limitedStarted = JsonDocument.Parse(
            await limitedStart.Content.ReadAsStringAsync(cancellationToken));
        var limitedTodoId = limitedStarted.RootElement.GetProperty("activeTodoId").GetGuid();
        using var forbiddenApprove = await limitedClient.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{limitedTodoId:D}/approve", limitedTenantToken,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { decision = "bypass" },
                    comment = "must be forbidden",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenApprove.StatusCode);
        using var forbiddenProblem = JsonDocument.Parse(
            await forbiddenApprove.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("authorization.permission_denied",
            forbiddenProblem.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertDangerousPatchesRejectedAsync(
        HttpClient client,
        string token,
        Guid todoId,
        CancellationToken cancellationToken)
    {
        using var invalidTypePatch = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", token,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { reason = 42 },
                    comment = "invalid type",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, invalidTypePatch.StatusCode);

        using var readOnlyPatch = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", token,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { reason = "changed" },
                    comment = "read only",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, readOnlyPatch.StatusCode);

        using var hiddenPatch = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", token,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { secret = "exposed" },
                    comment = "hidden patch",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, hiddenPatch.StatusCode);

        using var missingRequiredPatch = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", token,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new Dictionary<string, object?>(),
                    comment = "missing required decision",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, missingRequiredPatch.StatusCode);

        using var invalidPatch = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", token,
                new
                {
                    expectedRevision = 1,
                    fieldPatch = new { injected = "forbidden" },
                    comment = "invalid patch",
                    idempotencyKey = $"approve-{Guid.NewGuid():N}",
                }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, invalidPatch.StatusCode);
        using var problem = JsonDocument.Parse(await invalidPatch.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(WorkflowErrorCodes.SchemaInvalid, problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> LoginAsHostAdminAsync(
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
        return loginToken.AccessToken;
    }

    private static async Task<string> EnterAcmeTenantWithRolePermissionsAsync(
        HttpClient client,
        IReadOnlyCollection<string> workflowPermissions,
        CancellationToken cancellationToken)
    {
        var hostAdminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var roleCode = $"wf-bound-{Guid.NewGuid():N}".ToLowerInvariant();
        var username = $"wf-bound-{Guid.NewGuid():N}".ToLowerInvariant();
        var rolePermissions = new[]
            {
                "platform.dashboard.read",
                "tenancy.tenants.read",
                "tenancy.tenants.switch",
            }
            .Concat(workflowPermissions)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        using var createRoleResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/identity/roles", hostAdminToken,
                new CreateHostRoleRequest(roleCode, "工作流租户动作边界角色")),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var createdRole = await createRoleResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(createdRole);

        using var updatePermissionsResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Put, $"/api/v1/identity/roles/{createdRole.Id:D}/permissions", hostAdminToken,
                new ReplaceHostRolePermissionsRequest(rolePermissions, createdRole.Version)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updatePermissionsResponse.StatusCode);

        using var createUserResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/identity/users", hostAdminToken,
                new CreateHostUserRequest(username, "工作流租户动作边界用户", FullNetApiFactory.TestPassword)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUserResponse.StatusCode);
        var createdUser = await createUserResponse.Content
            .ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(createdUser);

        using var getRolesRequest = Authorized(
            HttpMethod.Get, $"/api/v1/identity/users/{createdUser.Id:D}/roles", hostAdminToken);
        using var getRolesResponse = await client.SendAsync(getRolesRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getRolesResponse.StatusCode);
        var userRoles = await getRolesResponse.Content
            .ReadFromJsonAsync<HostUserRolesResponse>(cancellationToken);
        Assert.IsNotNull(userRoles);

        using var assignRoleResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Put, $"/api/v1/identity/users/{createdUser.Id:D}/roles", hostAdminToken,
                new ReplaceHostUserRolesRequest([createdRole.Id], userRoles.Version)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, assignRoleResponse.StatusCode);

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(loginToken);
        return await EnterAcmeTenantAsync(client, loginToken.AccessToken, cancellationToken);
    }

    private static async Task<string> EnterAcmeTenantAsync(
        HttpClient client,
        string hostAccessToken,
        CancellationToken cancellationToken)
    {
        using var availableRequest = Authorized(HttpMethod.Get, "/api/v1/tenancy/available", hostAccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        var acme = available.Single(tenant => tenant.Identifier == "acme");

        using var enterRequest = AuthorizedJson(
            HttpMethod.Put, "/api/v1/tenancy/context", hostAccessToken, new ChangeTenantContextRequest(acme.Id));
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(entered);
        return entered.AccessToken;
    }

    private static async Task VerifyLinearMultiApprovalAsync(
        HttpClient client,
        string token,
        Guid formVersionId,
        CancellationToken cancellationToken)
    {
        var definitionVersionId = await PublishLinearMultiApprovalDefinitionAsync(
            client, token, formVersionId, cancellationToken);
        using var start = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", token, new
            {
                definitionVersionId,
                businessType = "leave.multi-approval",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "multi-stage", secret = "classified" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, start.StatusCode,
            await start.Content.ReadAsStringAsync(cancellationToken));
        using var started = JsonDocument.Parse(await start.Content.ReadAsStringAsync(cancellationToken));
        var firstTodoId = started.RootElement.GetProperty("activeTodoId").GetGuid();
        var firstIdempotencyKey = $"approve-{Guid.NewGuid():N}";

        using var firstApprove = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{firstTodoId:D}/approve", token, new
            {
                expectedRevision = 1,
                fieldPatch = new { decision = "first-approved" },
                comment = "first stage",
                idempotencyKey = firstIdempotencyKey,
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstApprove.StatusCode,
            await firstApprove.Content.ReadAsStringAsync(cancellationToken));
        using var advanced = JsonDocument.Parse(await firstApprove.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("active", advanced.RootElement.GetProperty("statusKey").GetString());
        Assert.AreEqual(2, advanced.RootElement.GetProperty("revision").GetInt64());
        var secondTodoId = advanced.RootElement.GetProperty("activeTodoId").GetGuid();
        Assert.AreNotEqual(firstTodoId, secondTodoId);

        using var replay = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{firstTodoId:D}/approve", token, new
            {
                expectedRevision = 1,
                fieldPatch = new { decision = "first-approved" },
                comment = "first stage",
                idempotencyKey = firstIdempotencyKey,
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode,
            await replay.Content.ReadAsStringAsync(cancellationToken));
        using var replayed = JsonDocument.Parse(await replay.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(secondTodoId, replayed.RootElement.GetProperty("activeTodoId").GetGuid());

        using var secondApprove = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{secondTodoId:D}/approve", token, new
            {
                expectedRevision = 1,
                fieldPatch = new { decision = "final-approved" },
                comment = "final stage",
                idempotencyKey = $"approve-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondApprove.StatusCode,
            await secondApprove.Content.ReadAsStringAsync(cancellationToken));
        using var completed = JsonDocument.Parse(await secondApprove.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("completed", completed.RootElement.GetProperty("statusKey").GetString());
        Assert.AreEqual(3, completed.RootElement.GetProperty("revision").GetInt64());
        Assert.AreEqual(JsonValueKind.Null, completed.RootElement.GetProperty("activeTodoId").ValueKind);
    }

    private static async Task<Guid> PublishLinearMultiApprovalDefinitionAsync(
        HttpClient client,
        string token,
        Guid formVersionId,
        CancellationToken cancellationToken)
    {
        var fieldPolicies = new Dictionary<string, string>
        {
            ["reason"] = "readOnly",
            ["secret"] = "hidden",
            ["decision"] = "required",
        };
        using var create = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/definitions", token, new
            {
                definitionKey = $"runtime.multi.{Guid.NewGuid():N}",
                draft = new
                {
                    schemaVersion = 1,
                    nodes = new object[]
                    {
                        new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "first" } } },
                        new { nodeKey = "first", nodeTypeKey = "human.approval", nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "second" }, fieldPolicies } },
                        new { nodeKey = "second", nodeTypeKey = "human.approval", nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "end" }, fieldPolicies } },
                        new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = Array.Empty<string>() } },
                    },
                },
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode,
            await create.Content.ReadAsStringAsync(cancellationToken));
        using var definition = JsonDocument.Parse(await create.Content.ReadAsStringAsync(cancellationToken));

        using var publish = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post,
                $"/api/v1/workflow/definitions/{definition.RootElement.GetProperty("id").GetGuid():D}/publish",
                token, new { expectedRevision = 1, formVersionId }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publish.StatusCode,
            await publish.Content.ReadAsStringAsync(cancellationToken));
        using var version = JsonDocument.Parse(await publish.Content.ReadAsStringAsync(cancellationToken));
        return version.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>验收启动前置和审批尾部抄送、实例级去重、本人查询与幂等已读。</summary>
    /// <param name="client">已绑定目标数据库提供程序的测试客户端。</param>
    /// <param name="publisherToken">具备定义发布、实例启动和待办动作权限的访问令牌。</param>
    /// <param name="recipientToken">具备抄送读取、已读和实例读取权限的收件人令牌。</param>
    /// <param name="recipientUserId">定义发布时校验并在运行时接收抄送的活动用户。</param>
    /// <param name="formVersionId">已经发布的不可变表单版本。</param>
    /// <param name="cancellationToken">测试取消令牌。</param>
    private static async Task VerifyCcRuntimeAsync(
        HttpClient client,
        string publisherToken,
        string recipientToken,
        Guid recipientUserId,
        Guid formVersionId,
        CancellationToken cancellationToken)
    {
        var definitionVersionId = await PublishCcDefinitionAsync(
            client,
            publisherToken,
            recipientUserId,
            formVersionId,
            cancellationToken);
        using var start = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", publisherToken, new
            {
                definitionVersionId,
                businessType = "leave.cc",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "cc-stage", secret = "classified" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, start.StatusCode,
            await start.Content.ReadAsStringAsync(cancellationToken));
        using var started = JsonDocument.Parse(await start.Content.ReadAsStringAsync(cancellationToken));
        var instanceId = started.RootElement.GetProperty("id").GetGuid();
        var todoId = started.RootElement.GetProperty("activeTodoId").GetGuid();

        using var approve = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", publisherToken, new
            {
                expectedRevision = 1,
                fieldPatch = new { decision = "approved" },
                comment = "cc runtime",
                idempotencyKey = $"approve-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, approve.StatusCode,
            await approve.Content.ReadAsStringAsync(cancellationToken));

        using var mine = await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/workflow/cc/mine", recipientToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode,
            await mine.Content.ReadAsStringAsync(cancellationToken));
        using var records = JsonDocument.Parse(await mine.Content.ReadAsStringAsync(cancellationToken));
        var cc = records.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("instanceId").GetGuid() == instanceId);
        Assert.AreEqual(JsonValueKind.Null, cc.GetProperty("readAtUtc").ValueKind);
        var ccId = cc.GetProperty("id").GetGuid();

        using var participantRead = await client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/workflow/instances/{instanceId:D}", recipientToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, participantRead.StatusCode,
            await participantRead.Content.ReadAsStringAsync(cancellationToken));

        using var concealedCrossUserRead = await client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/workflow/cc/{ccId:D}/read", publisherToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, concealedCrossUserRead.StatusCode);

        using var markRead = await client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/workflow/cc/{ccId:D}/read", recipientToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, markRead.StatusCode,
            await markRead.Content.ReadAsStringAsync(cancellationToken));
        using var replay = await client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/workflow/cc/{ccId:D}/read", recipientToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode,
            await replay.Content.ReadAsStringAsync(cancellationToken));

        using var logs = await client.SendAsync(
            Authorized(HttpMethod.Get,
                $"/api/v1/workflow/instances/{instanceId:D}/execution-logs",
                recipientToken), cancellationToken);
        using var executionLogs = JsonDocument.Parse(await logs.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(2, executionLogs.RootElement.EnumerateArray()
            .Count(item => item.GetProperty("transitionKey").GetString() == "node.notify.cc"));

        using var rejectedStart = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", publisherToken, new
            {
                definitionVersionId,
                businessType = "leave.cc.rejected",
                businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { reason = "cc-reject", secret = "classified" },
                idempotencyKey = $"start-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, rejectedStart.StatusCode,
            await rejectedStart.Content.ReadAsStringAsync(cancellationToken));
        using var rejectedInstance = JsonDocument.Parse(
            await rejectedStart.Content.ReadAsStringAsync(cancellationToken));
        var rejectedInstanceId = rejectedInstance.RootElement.GetProperty("id").GetGuid();
        var rejectedTodoId = rejectedInstance.RootElement.GetProperty("activeTodoId").GetGuid();

        using var reject = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, $"/api/v1/workflow/todos/{rejectedTodoId:D}/reject", publisherToken, new
            {
                expectedRevision = 1,
                fieldPatch = new { decision = "rejected" },
                comment = "cc reject path",
                idempotencyKey = $"reject-{Guid.NewGuid():N}",
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, reject.StatusCode,
            await reject.Content.ReadAsStringAsync(cancellationToken));

        using var rejectedLogs = await client.SendAsync(
            Authorized(HttpMethod.Get,
                $"/api/v1/workflow/instances/{rejectedInstanceId:D}/execution-logs",
                recipientToken), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, rejectedLogs.StatusCode,
            await rejectedLogs.Content.ReadAsStringAsync(cancellationToken));
        using var rejectedExecutionLogs = JsonDocument.Parse(
            await rejectedLogs.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(1, rejectedExecutionLogs.RootElement.EnumerateArray()
            .Count(item => item.GetProperty("transitionKey").GetString() == "node.notify.cc"));
    }

    /// <summary>发布包含前置和尾部抄送的线性审批定义。</summary>
    /// <param name="client">已绑定目标数据库提供程序的测试客户端。</param>
    /// <param name="token">具备定义管理权限的访问令牌。</param>
    /// <param name="recipientUserId">活动抄送人标识。</param>
    /// <param name="formVersionId">绑定的不可变表单版本。</param>
    /// <param name="cancellationToken">测试取消令牌。</param>
    /// <returns>发布后的定义版本标识。</returns>
    private static async Task<Guid> PublishCcDefinitionAsync(
        HttpClient client,
        string token,
        Guid recipientUserId,
        Guid formVersionId,
        CancellationToken cancellationToken)
    {
        var fieldPolicies = new Dictionary<string, string>
        {
            ["reason"] = "readOnly",
            ["secret"] = "hidden",
            ["decision"] = "required",
        };
        using var create = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/definitions", token, new
            {
                definitionKey = $"runtime.cc.{Guid.NewGuid():N}",
                draft = new
                {
                    schemaVersion = 1,
                    nodes = new object[]
                    {
                        new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "before" } } },
                        new { nodeKey = "before", nodeTypeKey = "notify.cc", nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "approve" }, recipientUserIds = new[] { recipientUserId } } },
                        new { nodeKey = "approve", nodeTypeKey = "human.approval", nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "after" }, fieldPolicies } },
                        new { nodeKey = "after", nodeTypeKey = "notify.cc", nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "end" }, recipientUserIds = new[] { recipientUserId } } },
                        new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = Array.Empty<string>() } },
                    },
                },
            }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode,
            await create.Content.ReadAsStringAsync(cancellationToken));
        using var definition = JsonDocument.Parse(await create.Content.ReadAsStringAsync(cancellationToken));
        using var publish = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post,
                $"/api/v1/workflow/definitions/{definition.RootElement.GetProperty("id").GetGuid():D}/publish",
                token,
                new { expectedRevision = 1, formVersionId }), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publish.StatusCode,
            await publish.Content.ReadAsStringAsync(cancellationToken));
        using var version = JsonDocument.Parse(await publish.Content.ReadAsStringAsync(cancellationToken));
        return version.RootElement.GetProperty("id").GetGuid();
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
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/definitions/recipient-candidates", HttpMethod.Get,
            "workflowListRecipientCandidates", "WorkflowDefinitions", 200, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/cc/mine", HttpMethod.Get,
            "workflowListMyCc", "WorkflowCc", 200, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document.RootElement, "/api/v1/workflow/cc/{ccId}/read", HttpMethod.Post,
            "workflowMarkCcRead", "WorkflowCc", 200, "application/json");
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
