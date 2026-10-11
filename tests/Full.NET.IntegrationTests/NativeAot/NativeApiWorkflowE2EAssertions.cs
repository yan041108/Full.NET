using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;

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
            NativeAotTestTimeouts.ProcessStartup,
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(
                client,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var assets = await PublishAssetsAsync(client, token, cancellationToken)
            .ConfigureAwait(false);

        await VerifyLinearApprovalAsync(
                client,
                token,
                assets,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        await VerifyTerminalRejectionAsync(
                client,
                token,
                assets,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        // 最后切换租户，避免会话代次变化使前面的 Host 令牌失效。
        var tenantToken = await NativeApiE2EAssertions.EnterLocalTenantAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyTenantMembershipAsync(client, tenantToken, host.LogFilePath, cancellationToken)
            .ConfigureAwait(false);

        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    private static async Task VerifyLinearApprovalAsync(
        HttpClient client,
        string token,
        PublishedWorkflowAssets assets,
        string? nativeLogFilePath,
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
                cancellationToken,
                nativeLogFilePath)
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
        string? nativeLogFilePath,
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
                cancellationToken,
                nativeLogFilePath)
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
        CancellationToken cancellationToken,
        Guid? assigneeId = null)
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
        var assigneePolicy = assigneeId.HasValue
            ? JsonSerializer.SerializeToElement(new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { assigneeId.Value } } } })
            : JsonSerializer.SerializeToElement(new { sources = new[] { new { resolverKindKey = "initiator" } } });
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
                            config = new { nextNodeKeys = new[] { "second" }, fieldPolicies, assigneePolicy },
                        },
                        new
                        {
                            nodeKey = "second",
                            nodeTypeKey = "human.approval",
                            nodeSchemaVersion = 1,
                            config = new { nextNodeKeys = new[] { "end" }, fieldPolicies, assigneePolicy },
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

    /// <summary>以真实现代成员验证原生候选、预览、发布、运行时解析、改派与撤销后的拒绝。</summary>
    private static async Task VerifyTenantMembershipAsync(
        HttpClient client, string token, string logPath, CancellationToken cancellationToken)
    {
        using var provision = await client.SendAsync(AuthorizedJson(HttpMethod.Post,
            "/api/v1/identity/tenant-members/provision", token, new
            {
                username = $"native-wf-{Guid.NewGuid():N}", displayName = "原生审批成员",
                password = NativeApiE2EAssertions.AdminPassword, memberRole = "Member", email = (string?)null,
            }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(provision, HttpStatusCode.OK,
            "Provision native Workflow tenant member", cancellationToken, logPath).ConfigureAwait(false);
        using var member = JsonDocument.Parse(await provision.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var userId = member.RootElement.GetProperty("userId").GetGuid();
        using var nextProvision = await client.SendAsync(AuthorizedJson(HttpMethod.Post,
            "/api/v1/identity/tenant-members/provision", token, new
            {
                username = $"native-wf-next-{Guid.NewGuid():N}", displayName = "原生改派成员",
                password = NativeApiE2EAssertions.AdminPassword, memberRole = "Member", email = (string?)null,
            }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(nextProvision, HttpStatusCode.OK,
            "Provision distinct native reassignment member", cancellationToken, logPath).ConfigureAwait(false);
        using var nextMember = JsonDocument.Parse(await nextProvision.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var nextUserId = nextMember.RootElement.GetProperty("userId").GetGuid();
        var memberId = nextMember.RootElement.GetProperty("id").GetGuid();
        var memberVersion = nextMember.RootElement.GetProperty("version").GetInt32();
        Assert.AreNotEqual(userId, nextUserId);
        using var candidatesRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/workflow/definitions/recipient-candidates?page=1&pageSize=100");
        candidatesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var candidatesResponse = await client.SendAsync(candidatesRequest, cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(candidatesResponse, HttpStatusCode.OK,
            "List native member candidates", cancellationToken, logPath).ConfigureAwait(false);
        using var candidates = JsonDocument.Parse(await candidatesResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Assert.IsTrue(candidates.RootElement.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == userId));
        using var preview = await client.SendAsync(AuthorizedJson(HttpMethod.Post,
            "/api/v1/workflow/definitions/assignee-preview", token,
            new { assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { userId } } } } }),
            cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(preview, HttpStatusCode.OK,
            "Preview native member assignee", cancellationToken, logPath).ConfigureAwait(false);
        var assets = await PublishAssetsAsync(client, token, cancellationToken, userId).ConfigureAwait(false);
        using var start = await client.SendAsync(AuthorizedJson(HttpMethod.Post, "/api/v1/workflow/instances", token,
            new { definitionVersionId = assets.DefinitionVersionId, businessType = "native.workflow.member",
                businessId = Guid.NewGuid().ToString("N"), initialValues = new { reason = "native member" },
                idempotencyKey = $"member-{Guid.NewGuid():N}" }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(start, HttpStatusCode.Created,
            "Start native member-assigned Workflow", cancellationToken, logPath).ConfigureAwait(false);
        using var instance = JsonDocument.Parse(await start.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var instanceId = instance.RootElement.GetProperty("id").GetGuid();
        var revision = instance.RootElement.GetProperty("revision").GetInt32();
        using var reassigned = await client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/workflow/instances/{instanceId:D}/reassign", token,
            new { assigneeUserId = nextUserId, expectedRevision = revision, reason = "原生成员交接", idempotencyKey = $"reassign-{Guid.NewGuid():N}" }),
            cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(reassigned, HttpStatusCode.OK,
            "Reassign native Workflow to active member", cancellationToken, logPath).ConfigureAwait(false);
        using var updated = JsonDocument.Parse(await reassigned.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        revision = updated.RootElement.GetProperty("revision").GetInt32();
        using var removal = await client.SendAsync(AuthorizedJson(HttpMethod.Delete,
            $"/api/v1/identity/tenant-members/{memberId:D}?version={memberVersion}", token, new { }), cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(removal, HttpStatusCode.OK,
            "Remove native Workflow tenant member", cancellationToken, logPath).ConfigureAwait(false);
        using var rejected = await client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/workflow/instances/{instanceId:D}/reassign", token,
            new { assigneeUserId = nextUserId, expectedRevision = revision, reason = "已撤销成员", idempotencyKey = $"removed-{Guid.NewGuid():N}" }),
            cancellationToken).ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(rejected, HttpStatusCode.BadRequest,
            "Reject removed native Workflow member", cancellationToken, logPath).ConfigureAwait(false);
        // 相同办理人也会返回 400，必须核对资格错误码，避免错误拒绝路径造成假绿。
        using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(WorkflowErrorCodes.TodoAssigneeNotFound, problem.RootElement.GetProperty("code").GetString());
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
