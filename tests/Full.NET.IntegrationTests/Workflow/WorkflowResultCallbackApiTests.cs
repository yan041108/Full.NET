using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证活动最终回填、个人证据分离、并行局部输出及SLA系统处置。</summary>
[TestClass]
public sealed class WorkflowResultCallbackApiTests
{
    [TestMethod]
    public async Task Result_callback_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Result_callback_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        var clock = new CallbackClock();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => {
            services.Replace(ServiceDescriptor.Scoped<WorkflowInstanceManagementService>(scope => ActivatorUtilities.CreateInstance<WorkflowInstanceManagementService>(scope, clock)));
            services.Replace(ServiceDescriptor.Scoped<WorkflowSlaProcessor>(scope => ActivatorUtilities.CreateInstance<WorkflowSlaProcessor>(scope, clock)));
            services.Replace(ServiceDescriptor.Scoped<WorkflowActivityWorkProcessor>(scope => ActivatorUtilities.CreateInstance<WorkflowActivityWorkProcessor>(scope, clock)));
        });
        await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var permissions = new[] { WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate,
            WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead,
            WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReject, WorkflowPermissions.TodosComplete,
            WorkflowPermissions.TodosVote, WorkflowPermissions.TodosSign, WorkflowPermissions.TodosCountersign,
            WorkflowPermissions.ClaimPoolsRead, WorkflowPermissions.ClaimPoolsClaim, HostFilePermissions.Upload };
        var people = new[] { await factory.CreateHostIdentityAsync("callback.first." + Guid.NewGuid().ToString("N"), permissions),
            await factory.CreateHostIdentityAsync("callback.second." + Guid.NewGuid().ToString("N"), permissions) };
        using var first = factory.CreateClientForHost("localhost"); using var second = factory.CreateClientForHost("localhost");
        first.DefaultRequestHeaders.Authorization = new("Bearer", people[0].AccessToken); second.DefaultRequestHeaders.Authorization = new("Bearer", people[1].AccessToken);
        var clients = new[] { first, second };
        async Task<Guid> Form(object[] fields, HttpClient? owner = null) {
            owner ??= first;
            var created = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "callback." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
                sections = new[] { new { sectionKey = "main", fields } } } }, HttpStatusCode.Created);
            return (await PostAsync(owner, $"/api/v1/workflow/forms/{created.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        }
        var form = await Form([new { fieldKey = "accepted", fieldTypeKey = "switch", required = false, constraints = new { } },
            new { fieldKey = "voteAccepted", fieldTypeKey = "switch", required = false, constraints = new { } },
            new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } },
            new { fieldKey = "evidence", fieldTypeKey = "attachment", required = false, constraints = new { maxCount = 1, maxSizeBytes = 1024, allowedExtensions = new[] { "png" } } }]);
        var ballotForm = await Form([new { fieldKey = "answer", fieldTypeKey = "text", required = false, constraints = new { } }]);
        object Sources(params Guid[] users) => new { sources = new[] { new { resolverKindKey = "specified_users", userIds = users } } };
        Dictionary<string, object> Configuration(string kind, bool multiple = false, string target = "accepted") {
            var config = new Dictionary<string, object> { ["nextNodeKeys"] = new[] { "route" }, ["resultCallbackFieldKey"] = target,
                ["fieldPolicies"] = new Dictionary<string, string> { [target] = "editable" },
                ["listeners"] = new[] { new { listenerKey = "done", eventKey = "completed", actionKey = "notify.initiator", actionVersion = 1 },
                    new { listenerKey = "failed", eventKey = "rejected", actionKey = "notify.initiator", actionVersion = 1 } } };
            var members = multiple ? people.Select(person => person.UserId).ToArray() : [people[0].UserId];
            if (kind == "human.group_approval") {
                config["groupApprovalPolicy"] = new { modeKey = "all" };
                config["groups"] = people.Select((person, index) => new { groupKey = "group_" + index, assigneePolicy = Sources(person.UserId) }).ToArray();
            } else config["assigneePolicy"] = Sources(members);
            if (kind == "human.approval" && multiple) config["approvalPolicy"] = new { modeKey = "all", approverUserIds = Array.Empty<Guid>() };
            if (kind == "human.task") config["taskPolicy"] = new { completionModeKey = "all" };
            if (kind == "human.claim") config["claimPolicy"] = new { allowRelease = true };
            if (kind == "human.vote") {
                config["votePolicy"] = new { ballotFormVersionId = ballotForm, participationPercentage = 100, approvalPercentage = 100, allowAbstain = true, resultFieldKey = "voteAccepted" };
                ((Dictionary<string, string>)config["fieldPolicies"])["voteAccepted"] = "editable";
            }
            return config;
        }
        async Task<Guid> Publish(string kind, Dictionary<string, object> config) => await PublishNodesAsync(first, form, [
            Node("start", "start", new { nextNodeKeys = new[] { "review" } }), Node("review", kind, config),
            Node("route", "gateway.exclusive", new { nextNodeKeys = new[] { "yes", "no" }, branches = new[] { new { branchKey = "yes", nextNodeKey = "yes", condition = new { fieldKey = "accepted", @operator = "equals", value = true } } }, defaultNextNodeKey = "no" }),
            Node("yes", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("no", "control.empty", new { nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        async Task<Guid> Start(Guid version, bool initial = false, Guid? file = null) => (await PostAsync(first, "/api/v1/workflow/instances", new { definitionVersionId = version,
            businessType = "callback.test", businessId = Guid.NewGuid().ToString("N"), initialValues = new { accepted = initial, voteAccepted = false, title = "keep", evidence = file is { } value ? new[] { value } : Array.Empty<Guid>() },
            idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task<Guid> Todo(Guid instance, int person) => await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor AND StatusKey='active'", new { instance, actor = people[person].UserId });
        async Task<JsonElement> Detail(HttpClient client, Guid todo) => await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
        async Task<JsonElement> Values(Guid instance) { var json = await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }); using var document = JsonDocument.Parse(json!); return document.RootElement.Clone(); }
        async Task<string?> Status(Guid instance) => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance });
        async Task Act(Guid instance, int person, string action = "approve") {
            var todo = await Todo(instance, person); var detail = await Detail(clients[person], todo);
            await PostAsync(clients[person], $"/api/v1/workflow/todos/{todo:D}/{action}", new { expectedRevision = detail.GetProperty("revision").GetInt64(),
                expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = Guid.NewGuid().ToString("N") });
        }
        async Task<int> CallbackAudits(Guid instance) {
            var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@instance AND ActorUserId IS NULL AND DetailJson LIKE '%result_callback%'", new { instance });
            await using var scope = factory.Services.CreateAsyncScope();
            var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
            var rows = await db.QueryAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.ActivityListenerRequested });
            var matching = rows.Select(payload => serializer.Deserialize<WorkflowActivityListenerRequestedIntegrationEvent>(payload)).Where(item => item.InstanceId == instance).ToArray();
            Assert.AreEqual(count, matching.Length, "监听器只在真实活动收敛后投递一次；个人提交和重放不重复。");
            foreach (var item in matching) {
                Assert.AreEqual("notify.initiator", item.ActionKey); Assert.AreEqual(1, item.ActionVersion);
                Assert.AreEqual(await db.ExecuteScalarAsync<Guid>("SELECT StartedById FROM fn_workflow_instance WHERE Id=@instance", new { instance }), item.RecipientUserId);
                Assert.AreEqual(await Status(instance) == "rejected" ? "rejected" : "completed", item.EventKey);
            }
            return count;
        }
        clock.UtcNow = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);

        // 派生字段与个人意见/附件快照分开；同一回执重放不能再写一次结果。
        Guid fileId;
        using (var body = new MultipartFormDataContent()) {
            var bytes = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII="));
            bytes.Headers.ContentType = new("image/png"); body.Add(bytes, "file", "callback.png");
            using var response = await first.PostAsync("/api/v1/files/host-files/", body); Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
            fileId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        var signedConfig = Configuration("human.approval"); signedConfig["signRequired"] = true;
        var single = await Start(await Publish("human.approval", signedConfig), file: fileId);
        var singleTodo = await Todo(single, 0); var input = await Detail(first, singleTodo);
        var request = new { expectedRevision = input.GetProperty("revision").GetInt64(), expectedFormRevision = input.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { },
            signatureFileId = fileId, idempotencyKey = Guid.NewGuid().ToString("N") };
        var accepted = await PostAsync(first, $"/api/v1/workflow/todos/{singleTodo:D}/approve", request);
        var replay = await PostAsync(first, $"/api/v1/workflow/todos/{singleTodo:D}/approve", request); Assert.AreEqual(accepted.GetRawText(), replay.GetRawText());
        Assert.IsTrue((await Values(single)).GetProperty("accepted").GetBoolean()); Assert.AreEqual("completed", await Status(single)); Assert.AreEqual(1, await CallbackAudits(single));
        var personal = await Detail(first, singleTodo); Assert.IsFalse(personal.GetProperty("submission").GetProperty("accepted").GetBoolean());
        var signedOutput = await db.ExecuteScalarAsync<string>("SELECT OutputJson FROM fn_workflow_todo_view WHERE Id=@singleTodo", new { singleTodo });
        var signatureHash = await db.ExecuteScalarAsync<string>("SELECT SubmissionHash FROM fn_workflow_action_evidence WHERE TodoId=@singleTodo", new { singleTodo });
        Assert.AreEqual(WorkflowSignatureCoordinator.HashSubmission(signedOutput!), signatureHash);
        Assert.AreEqual(personal.GetProperty("submissionRevision").GetInt64(), await db.ExecuteScalarAsync<long>("SELECT FormRevision FROM fn_workflow_action_evidence WHERE TodoId=@singleTodo", new { singleTodo }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_form_submission_attachment WHERE InstanceId=@single AND FileId=@fileId", new { single, fileId }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@single AND NodeKey='yes'", new { single }));
        var rejected = await Start(await Publish("human.approval", Configuration("human.approval")), initial: true); await Act(rejected, 0, "reject");
        Assert.IsFalse((await Values(rejected)).GetProperty("accepted").GetBoolean()); Assert.AreEqual("rejected", await Status(rejected));

        foreach (var kind in new[] { "human.approval", "human.group_approval", "human.task" }) {
            var instance = await Start(await Publish(kind, Configuration(kind, multiple: true))); var action = kind == "human.task" ? "complete" : "approve";
            await Act(instance, 0, action); Assert.AreEqual("active", await Status(instance)); Assert.IsFalse((await Values(instance)).GetProperty("accepted").GetBoolean()); Assert.AreEqual(0, await CallbackAudits(instance));
            await Act(instance, 1, action); Assert.AreEqual("completed", await Status(instance)); Assert.IsTrue((await Values(instance)).GetProperty("accepted").GetBoolean()); Assert.AreEqual(1, await CallbackAudits(instance));
        }
        var claim = await Start(await Publish("human.claim", Configuration("human.claim")));
        var pool = await db.QuerySingleAsync<(Guid Id, long Revision)>("SELECT Id,Revision FROM fn_workflow_claim WHERE InstanceId=@claim", new { claim });
        await PostAsync(first, $"/api/v1/workflow/claim-pools/{pool.Id:D}/claim", new { expectedRevision = pool.Revision, idempotencyKey = Guid.NewGuid().ToString("N") });
        Assert.IsFalse((await Values(claim)).GetProperty("accepted").GetBoolean()); Assert.AreEqual(0, await CallbackAudits(claim)); await Act(claim, 0, "complete"); Assert.IsTrue((await Values(claim)).GetProperty("accepted").GetBoolean());

        var countersigned = await Start(await Publish("human.approval", Configuration("human.approval")));
        var originTodo = await Todo(countersigned, 0); var origin = await Detail(first, originTodo);
        await PostAsync(first, $"/api/v1/workflow/todos/{originTodo:D}/countersign", new { directionKey = "after", assigneeUserIds = new[] { people[1].UserId },
            expectedRevision = origin.GetProperty("revision").GetInt64(), idempotencyKey = Guid.NewGuid().ToString("N") });
        await Act(countersigned, 0); Assert.IsFalse((await Values(countersigned)).GetProperty("accepted").GetBoolean()); Assert.AreEqual(0, await CallbackAudits(countersigned));
        await Act(countersigned, 1); Assert.IsTrue((await Values(countersigned)).GetProperty("accepted").GetBoolean()); Assert.AreEqual(1, await CallbackAudits(countersigned));

        var vote = await Start(await Publish("human.vote", Configuration("human.vote", multiple: true)));
        for (var person = 0; person < 2; person++) {
            var todo = await Todo(vote, person); var detail = await Detail(clients[person], todo);
            await PostAsync(clients[person], $"/api/v1/workflow/todos/{todo:D}/vote", new { expectedRevision = detail.GetProperty("revision").GetInt64(),
                expectedBallotRevision = detail.GetProperty("voteForm").GetProperty("revision").GetInt64(), choiceKey = "yes", values = new { answer = "own" + person }, idempotencyKey = Guid.NewGuid().ToString("N") });
            Assert.AreEqual(person == 1, (await Values(vote)).GetProperty("accepted").GetBoolean());
        }
        Assert.IsTrue((await Values(vote)).GetProperty("voteAccepted").GetBoolean()); Assert.AreEqual(1, await CallbackAudits(vote));
        var leftConfig = Configuration("human.approval"); leftConfig["nextNodeKeys"] = new[] { "join" };
        var rightConfig = Configuration("human.approval", target: "voteAccepted"); rightConfig["nextNodeKeys"] = new[] { "join" }; rightConfig["assigneePolicy"] = Sources(people[1].UserId);
        var parallelVersion = await PublishNodesAsync(first, form, [Node("start", "start", new { nextNodeKeys = new[] { "fork" } }),
            Node("fork", "gateway.parallel", new { gatewayRoleKey = "fork", joinNodeKey = "join", branches = new[] { new { branchKey = "left", nextNodeKey = "left" }, new { branchKey = "right", nextNodeKey = "right" } } }),
            Node("left", "human.approval", leftConfig), Node("right", "human.approval", rightConfig),
            Node("join", "gateway.parallel", new { gatewayRoleKey = "join", forkNodeKey = "fork", nextNodeKeys = new[] { "end" } }), Node("end", "end", new { })]);
        var parallel = await Start(parallelVersion); await Act(parallel, 0);
        Assert.IsFalse((await Values(parallel)).GetProperty("accepted").GetBoolean());
        Assert.IsFalse((await Detail(second, await Todo(parallel, 1))).GetProperty("submission").GetProperty("accepted").GetBoolean());
        await Act(parallel, 1); Assert.AreEqual("completed", await Status(parallel));
        Assert.IsTrue((await Values(parallel)).GetProperty("accepted").GetBoolean()); Assert.IsTrue((await Values(parallel)).GetProperty("voteAccepted").GetBoolean()); Assert.AreEqual(2, await CallbackAudits(parallel));

        using var tenantClient = factory.CreateClientForHost("localhost"); tenantClient.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var login = await PostAsync(tenantClient, "/api/v1/auth/login", new LoginRequest("admin", FullNetApiFactory.TestPassword));
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        var available = await tenantClient.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant = available!.Single(item => item.Identifier == "acme");
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient, login.GetProperty("accessToken").GetString()!, tenant.Id));
        var me = await tenantClient.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var tenantForm = await Form([new { fieldKey = "accepted", fieldTypeKey = "switch", required = false, constraints = new { } }], tenantClient);
        var tenantVersion = await PublishNodesAsync(tenantClient, tenantForm, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }),
            Node("review", "human.approval", new { nextNodeKeys = new[] { "end" }, resultCallbackFieldKey = "accepted", fieldPolicies = new { accepted = "editable" }, assigneePolicy = Sources(me.GetProperty("id").GetGuid()),
                listeners = new[] { new { listenerKey = "done", eventKey = "completed", actionKey = "notify.initiator", actionVersion = 1 } } }), Node("end", "end", new { })]);
        var tenantInstance = (await PostAsync(tenantClient, "/api/v1/workflow/instances", new { definitionVersionId = tenantVersion, businessType = "callback.tenant", businessId = Guid.NewGuid().ToString("N"),
            initialValues = new { accepted = false }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var tenantTodo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@tenantInstance AND StatusKey='active'", new { tenantInstance });
        var tenantDetail = await Detail(tenantClient, tenantTodo);
        await PostAsync(tenantClient, $"/api/v1/workflow/todos/{tenantTodo:D}/approve", new { expectedRevision = tenantDetail.GetProperty("revision").GetInt64(),
            expectedFormRevision = tenantDetail.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = Guid.NewGuid().ToString("N") });
        Assert.IsTrue((await Values(tenantInstance)).GetProperty("accepted").GetBoolean()); Assert.AreEqual(1, await CallbackAudits(tenantInstance));
        using (var foreign = await first.GetAsync($"/api/v1/workflow/instances/{tenantInstance:D}")) Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);

        var handledConfig = Configuration("human.approval"); handledConfig["assigneePolicy"] = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { people[0].UserId } } }, handling = new { whenInitiator = new { actionKey = "skip" } } };
        var handled = await Start(await Publish("human.approval", handledConfig)); Assert.AreEqual("completed", await Status(handled)); Assert.IsTrue((await Values(handled)).GetProperty("accepted").GetBoolean());
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@handled", new { handled }));
        var slaConfig = Configuration("human.approval"); slaConfig["slaPolicy"] = new { dueAfterMinutes = 1, pauseModeKey = "elapsed", actionKey = "auto_pass" };
        var sla = await Start(await Publish("human.approval", slaConfig)); clock.UtcNow = clock.UtcNow.AddMinutes(2);
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<WorkflowSlaProcessor>().ProcessPendingAsync(default));
        await using (var scope = factory.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);
        Assert.AreEqual("completed", await Status(sla)); Assert.IsTrue((await Values(sla)).GetProperty("accepted").GetBoolean()); Assert.AreEqual(1, await CallbackAudits(sla));

        // 消费真实MemoryPack消息，重复消息及不同MessageId重投均收敛到同一通知意图；不会伪造待办链接。
        await using (var scope = factory.Services.CreateAsyncScope()) {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
            var messages = await db.QueryAsync<(Guid Id, byte[] Payload)>("SELECT Id,Payload FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.ActivityListenerRequested });
            var message = messages.Single(row => serializer.Deserialize<WorkflowActivityListenerRequestedIntegrationEvent>(row.Payload).InstanceId == single);
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler>().Single(item => item.EventType == WorkflowNotificationIntegrationEventTypes.ActivityListenerRequested);
            var context = new IntegrationEventContext(message.Id, handler.EventType, 1, null, null, DateTimeOffset.UtcNow);
            await handler.HandleAsync(context, message.Payload, default); await handler.HandleAsync(context, message.Payload, default);
            await handler.HandleAsync(context with { MessageId = Guid.CreateVersion7() }, message.Payload, default);
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_notifications_intent WHERE SceneKey='workflow.activity.completed'"));
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(tenant.Id, tenant.Id.ToString("N"), tenant.Id.ToString("D")));
            var tenantMessage = messages.Single(row => serializer.Deserialize<WorkflowActivityListenerRequestedIntegrationEvent>(row.Payload).InstanceId == tenantInstance);
            var tenantContext = context with { MessageId = tenantMessage.Id, TenantId = tenant.Id };
            await handler.HandleAsync(tenantContext, tenantMessage.Payload, default);
            await handler.HandleAsync(tenantContext with { MessageId = Guid.CreateVersion7() }, tenantMessage.Payload, default);
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_notifications_intent WHERE SceneKey='workflow.activity.completed' AND TenantId=@id", new { id = tenant.Id }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_notifications_intent WHERE SceneKey='workflow.activity.completed' AND TenantId IS NULL"));
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().Clear();
        }
    }
    private static object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
    private sealed class CallbackClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
}
