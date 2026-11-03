using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using DbUp;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实源激活、事务Outbox及源信封收件双库验收；不将此场景计为目标协作执行通过。</summary>
[TestClass]
public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod]
    public async Task Collaboration_requests_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Collaboration_requests_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    [TestMethod]
    public async Task Collaboration_targets_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), true);
    [TestMethod]
    public async Task Collaboration_targets_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), true);
    [TestMethod]
    public async Task Collaboration_protocol_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), true, true);
    [TestMethod]
    public async Task Collaboration_protocol_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), true, true);
    [TestMethod]
    public async Task Collaboration_files_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), true, true, true);
    [TestMethod]
    public async Task Collaboration_files_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), true, true, true);

    private static async Task VerifyAsync(DatabaseProvider provider, string connection, bool withRelay = false, bool withProtocol = false, bool withFiles = false,
        bool withWorker = false, bool withChildren = false, WorkerTreeShape? treeShape = null, bool publicSource = false)
    {
        var fault = new InsertFault();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
        {
            var executor = services.Last(item => item.ServiceType == typeof(ICommandExecutor)).ImplementationFactory!;
            services.Replace(ServiceDescriptor.Scoped<ICommandExecutor>(scope => new FaultExecutor((ICommandExecutor)executor(scope), fault)));
        });
        await factory.InitializeAsync(useSchemaTemplate: false);
        string[] permissions = [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.InstancesStart, WorkflowPermissions.FormAttachmentsUpload, WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesList, WorkflowPermissions.InstancesCancel, WorkflowPermissions.InstancesPause, WorkflowPermissions.InstancesResume, WorkflowPermissions.InstancesRecover,
            WorkflowPermissions.ClaimPoolsRead, WorkflowPermissions.RecoveryTasksRead,
            WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosReject,
            WorkflowPermissions.ConnectorCallsRead, WorkflowPermissions.ConnectorCallsReconcile,
            WorkflowPermissions.CollaborationGrantsRead, WorkflowPermissions.CollaborationGrantsPropose,
            WorkflowPermissions.CollaborationGrantsAccept, WorkflowPermissions.CollaborationGrantsRevoke, "tenancy.tenants.switch"];
        if (publicSource) permissions = permissions.Concat([WorkflowPermissions.DefinitionsRead, WorkflowPermissions.DefinitionsUpdate]).ToArray();
        var hostIdentity = await factory.CreateHostIdentityAsync("collab.request.source." + Guid.CreateVersion7().ToString("N"), permissions);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new("Bearer", hostIdentity.AccessToken);
        using var owner = factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var administrator = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin"); owner.DefaultRequestHeaders.Authorization = new("Bearer", administrator);
        var tenant = (await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, administrator, tenant.Id));
        var name = "collab.request.member." + Guid.CreateVersion7().ToString("N");
        var memberIdentity = await factory.CreateHostIdentityAsync(name, permissions, password: FullNetApiFactory.TestPassword);
        var email = name + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(memberIdentity.UserId, email, name);
        var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
        using var member = factory.CreateClientForHost("localhost"); member.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var memberToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, name); member.DefaultRequestHeaders.Authorization = new("Bearer", memberToken);
        await PostAsync(member, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, memberToken, tenant.Id));
        if (withFiles && !withWorker) await DeniedUploadPermissionAsync(factory);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        if (withFiles && !withWorker) await VerifyWorkflowDirectoryRepairAsync(factory, db);
        if (withWorker)
        {
            foreach (var scenario in treeShape is not null ? new[] { "worker_success", "worker_cancel", "worker_revoked", "worker_budget_denied" }
                : withChildren ? new[] { "worker_success", "worker_cancel", "worker_budget_denied" } : new[] { "worker_success", "worker_cancel" })
            {
                await CaseAsync(factory, db, host, member, null, tenant.Id, memberIdentity.UserId, fault, true, scenario, worker: new(withChildren, withFiles, treeShape, publicSource));
                await CaseAsync(factory, db, member, host, tenant.Id, null, hostIdentity.UserId, fault, true, scenario, worker: new(withChildren, withFiles, treeShape, publicSource));
            }
            await RecoverWorkerAuditAsync(provider, connection, db);
            return;
        }
        await CaseAsync(factory, db, host, member, null, tenant.Id, memberIdentity.UserId, fault, withRelay);
        await CaseAsync(factory, db, member, host, tenant.Id, null, hostIdentity.UserId, fault, withRelay);
        if (withProtocol)
            foreach (var scenario in new[] { "success", "conflict", "uncreated", "revoked", "rejected", "unknown", "paused", "paused_revoked", "claims" })
            {
                await CaseAsync(factory, db, host, member, null, tenant.Id, memberIdentity.UserId, fault, true, scenario);
                await CaseAsync(factory, db, member, host, tenant.Id, null, hostIdentity.UserId, fault, true, scenario);
            }
        if (withFiles)
            foreach (var scenario in new[] { "files", "files_early_cancel", "files_paused" })
            {
                await CaseAsync(factory, db, host, member, null, tenant.Id, memberIdentity.UserId, fault, true, scenario, owner);
                await CaseAsync(factory, db, member, host, tenant.Id, null, hostIdentity.UserId, fault, true, scenario);
            }
        var calls = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_call");
        var deliveries = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_delivery");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DROP INDEX ix_fn_workflow_collaboration_delivery_pending ON fn_workflow_collaboration_delivery; ALTER TABLE fn_workflow_collaboration_call DROP CONSTRAINT fk_fn_workflow_collaboration_call_epoch"
            : "ALTER TABLE fn_workflow_collaboration_delivery DROP INDEX ix_fn_workflow_collaboration_delivery_pending; ALTER TABLE fn_workflow_collaboration_call DROP FOREIGN KEY fk_fn_workflow_collaboration_call_epoch");
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%333_WorkflowCollaborationRequests.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%333_WorkflowCollaborationRequests.sql'");
        Migrate333(provider, connection, 1); Migrate333(provider, connection, 0);
        if (withRelay)
        {
            var origins = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_origin");
            await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
                ? "DROP INDEX ix_fn_workflow_collaboration_admission_pending ON fn_workflow_collaboration_admission; ALTER TABLE fn_workflow_collaboration_origin DROP CONSTRAINT fk_fn_workflow_collaboration_origin_root"
                : "ALTER TABLE fn_workflow_collaboration_admission DROP INDEX ix_fn_workflow_collaboration_admission_pending; ALTER TABLE fn_workflow_collaboration_origin DROP FOREIGN KEY fk_fn_workflow_collaboration_origin_root");
            await db.ExecuteAsync(provider == DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%336_WorkflowCollaborationTargets.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%336_WorkflowCollaborationTargets.sql'");
            Migrate333(provider, connection, 1, true); Migrate333(provider, connection, 0, true);
            Assert.AreEqual(origins, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_origin"));
            await RecoverControlsAsync(provider, connection, db);
        }
        Assert.AreEqual(calls, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_call"));
        Assert.AreEqual(deliveries, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_delivery"));
        if (withFiles) await RecoverFilesAsync(provider, connection, db);
    }

    private static async Task CaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient source, HttpClient target,
        Guid? sourceTenant, Guid? targetTenant, Guid starter, InsertFault fault, bool withRelay, string scenario = "closing", HttpClient? outsider = null,
        BrowserFileFlow? browser = null, WorkerDeliveryFlow? worker = null)
    {
        var fileMode = scenario.StartsWith("files", StringComparison.Ordinal) || worker?.WithFiles == true;
        var sourceAsset = await AssetAsync(source, files: fileMode);
        var targetAsset = worker?.WithChildren == true ? await ChildAssetAsync(target, fileMode, worker.TreeShape == WorkerTreeShape.Deep ? 2 : 1,
            worker.TreeShape == WorkerTreeShape.Parallel) : await AssetAsync(target, withRelay && scenario != "claims", fileMode, scenario == "claims");
        Guid? sourceFile = fileMode ? await UploadViaWorkflowAsync(source, $"/api/v1/workflow/definition-versions/{sourceAsset.Version:D}/attachment-uploads?fieldKey=inputFiles", "source-owned.png", FileBytes) : null;
        const string grants = "/api/v1/workflow/collaboration-grants";
        var proposal = await PostAsync(source, grants, new { targetTenantId = targetTenant, targetScopeKey = targetTenant is null ? "host" : "tenant", idempotencyKey = Guid.CreateVersion7().ToString("N"),
            offer = new { schemaVersion = 1, sourceDefinitionId = sourceAsset.Definition, sourceFormVersionId = sourceAsset.Form, targetWorkflowKey = targetAsset.Key,
                inputFieldKeys = fileMode ? new[] { "title", "inputFiles", "rows" } : new[] { "title" }, outputFieldKeys = scenario == "closing" ? Array.Empty<string>() : fileMode ? new[] { "answer", "returnedFiles", "returnedRows" } : new[] { "answer" }, maximumTargetActivations = worker?.OfferedBudget ?? 64, expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) } });
        var grantId = proposal.GetProperty("id").GetGuid();
        var accepted = await PostAsync(target, $"{grants}/{grantId:D}/accept", new { expectedRevision = 1, idempotencyKey = "accept", acceptance = new {
            schemaVersion = 1, targetDefinitionVersionId = targetAsset.Version, starterUserId = starter, maximumActivities = worker?.GrantedBudget(scenario) ?? 32, allowedUserIds = new[] { starter },
            inputFields = fileMode ? new Dictionary<string, string> { ["title"] = "title", ["inputFiles"] = "inputFiles", ["rows"] = "rows" } : new Dictionary<string, string> { ["title"] = "title" },
            outputFields = scenario == "closing" ? new Dictionary<string, string>() : fileMode ? new Dictionary<string, string> { ["title"] = "answer", ["returnedFiles"] = "returnedFiles", ["rows"] = "returnedRows" } : new Dictionary<string, string> { ["title"] = "answer" } } });
        var policyHash = accepted.GetProperty("policyHash").GetString()!;
        var callNode = new string('c', 128); var endNode = new string('e', 128);
        var config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { endNode }, collaborationPolicy = new { schemaVersion = 1, grantId, grantRevision = 2, policyHash } });
        var draft = new WorkflowDefinitionDraft(1,
            [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { callNode } })),
             new(callNode, WorkflowCollaborationCallPolicy.NodeType, 1, config), new(endNode, "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
        if (worker?.PublicSource == true)
        {
            if (worker.AuthoringBrowser is { } authoring)
            {
                sourceAsset = sourceAsset with { Version = await authoring.PublishAsync(factory, source, sourceAsset, grantId) };
                var publishedModel = JsonSerializer.Deserialize((await db.ExecuteScalarAsync<string>("SELECT CanonicalJson FROM fn_workflow_definition_version WHERE Id=@id",
                    new { id = sourceAsset.Version }))!, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft)!;
                var publishedCall = publishedModel.Nodes.Single(node => node.NodeTypeKey == WorkflowCollaborationCallPolicy.NodeType);
                callNode = publishedCall.NodeKey; endNode = publishedCall.Config.GetProperty("nextNodeKeys")[0].GetString()!;
            }
            else
            sourceAsset = await PublishPublicSourceAsync(source, target, sourceTenant, targetAsset, db, sourceAsset, draft, scenario == "worker_success");
        }
        else
        {
        // 协议故障夹具仍可固定内部IR；公开能力另由真实草稿保存/发布入口验收。
        var artifact = WorkflowJsonCanonicalizer.Compile(JsonSerializer.SerializeToElement(draft, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft));
        var internalVersion = Guid.CreateVersion7();
        Assert.AreEqual(1, await db.ExecuteAsync("""
            INSERT INTO fn_workflow_definition_version
                (Id,DefinitionId,FormVersionId,VersionNumber,SchemaVersion,CanonicalJson,ContentHash,BusinessTitleTemplate,PublishedById,PublishedAtUtc)
            SELECT @internalVersion,DefinitionId,FormVersionId,VersionNumber+1,SchemaVersion,@json,@hash,BusinessTitleTemplate,PublishedById,PublishedAtUtc
            FROM fn_workflow_definition_version WHERE Id=@id
            """, new { internalVersion, json = artifact.CanonicalJson, hash = artifact.ContentHash, id = sourceAsset.Version }));
        sourceAsset = sourceAsset with { Version = internalVersion };
        }
        var initial = new Dictionary<string, object> { ["title"] = "shared", ["secret"] = "source-private" };
        if (sourceFile is { } originalFile) { initial["inputFiles"] = new[] { originalFile }; initial["rows"] = new[] { new { label = "source row", amount = 3 } }; }
        object Start(string key) => new { definitionVersionId = sourceAsset.Version, businessType = "collaboration.test", businessId = key, idempotencyKey = key, initialValues = initial };
        var before = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance");
        var beforeCalls = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_call");
        var beforeMessages = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowCollaborationIntegrationEventTypes.Requested });
        var previousFaultHits = fault.Hits;
        fault.Enabled = true;
        var rejected = await PostAsync(source, "/api/v1/workflow/instances", Start("fault-" + Guid.CreateVersion7().ToString("N")), HttpStatusCode.UnprocessableEntity);
        fault.Enabled = false;
        Assert.AreEqual("workflow.transition.invalid", rejected.GetProperty("code").GetString());
        Assert.AreEqual(previousFaultHits + 1, fault.Hits, "必须实际到达调用写入故障，不能把更早的拒绝当作事务回滚证明。");
        Assert.AreEqual(before, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"));
        Assert.AreEqual(beforeCalls, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_call"));
        Assert.AreEqual(beforeMessages, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowCollaborationIntegrationEventTypes.Requested }));
        var startRequest = Start(Guid.CreateVersion7().ToString("N"));
        var started = await PostAsync(source, "/api/v1/workflow/instances", startRequest, HttpStatusCode.Created);
        var replay = await PostAsync(source, "/api/v1/workflow/instances", startRequest, HttpStatusCode.Created);
        Assert.AreEqual(started.GetProperty("id").GetGuid(), replay.GetProperty("id").GetGuid());
        var instance = started.GetProperty("id").GetGuid(); var scopeKey = WorkflowCollaborationGrantPolicy.ScopeKey(sourceTenant);
        var call = await db.QuerySingleAsync<WorkflowCollaborationCallRecord>("SELECT * FROM fn_workflow_collaboration_call WHERE InstanceId=@instance", new { instance });
        Assert.AreEqual(scopeKey, call.TenantScopeKey); Assert.AreEqual(instance, call.ExecutionEpochId);
        Assert.AreEqual(worker?.GrantedBudget(scenario) ?? 32, call.ReservedActivities);
        Assert.AreEqual(callNode, call.NodeKey); Assert.AreEqual(endNode, call.ContinuationNodeKey);
        if (worker is not null)
        {
            await worker.RunAsync(factory, db, source, target, instance, call, grantId, scenario, before, sourceFile);
            return;
        }
        if (fileMode)
        {
            await FileCaseAsync(factory, db, source, target, sourceTenant, instance, call, grantId, sourceFile!.Value, fault, scenario, before, beforeMessages, outsider, browser);
            return;
        }
        Assert.AreEqual("{\"title\":\"shared\"}", call.ProjectionJson); StringAssert.Contains(call.InputJson, "source-private");
        Assert.AreEqual(call.BindingHash, WorkflowCollaborationCallPolicy.Hash(call.BindingJson));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE Id=@id AND InstanceId=@instance AND ExecutionEpochId=@instance AND NodeTypeKey='process.collaboration' AND StatusKey='active'", new { id = call.Id, instance }));
        Assert.AreEqual(32, await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }));
        Assert.AreEqual(before + 1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"), "接收请求不能启动目标实例");
        var message = await db.QuerySingleAsync<Message>("SELECT Id,MessageType,SchemaVersion,TenantId,TraceId,OccurredAtUtc,Payload FROM fn_outbox_message WHERE MessageType=@type AND Id NOT IN (SELECT EnvelopeMessageId FROM fn_workflow_collaboration_delivery)",
            new { type = WorkflowCollaborationIntegrationEventTypes.Requested });
        Assert.AreEqual(sourceTenant, message.TenantId);
        async Task Receive(IntegrationEventContext context, ReadOnlyMemory<byte> payload)
        {
            await using var consumer = factory.Services.CreateAsyncScope();
            var writer = consumer.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (sourceTenant is { } tenant) writer.SetTenant(new TenantContext(tenant, "source", "协作源")); else writer.SetHost();
            var handler = new WorkflowCollaborationRequestedHandler(consumer.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>(),
                consumer.ServiceProvider.GetRequiredService<IQueryExecutor>(), consumer.ServiceProvider.GetRequiredService<ICommandExecutor>(), consumer.ServiceProvider.GetRequiredService<IClock>(), writer);
            await handler.HandleAsync(context, payload, default);
            Assert.AreEqual(sourceTenant, writer.Id, "消费者不能切换到目标作用域");
        }
        var envelope = new IntegrationEventContext(message.Id, message.MessageType, message.SchemaVersion, message.TenantId, message.TraceId, message.OccurredAtUtc);
        await Assert.ThrowsAsync<InvalidDataException>(() => Receive(envelope with { TenantId = Guid.CreateVersion7() }, message.Payload));
        await Task.WhenAll(Receive(envelope, message.Payload), Receive(envelope, message.Payload));
        await Receive(envelope with { MessageId = Guid.CreateVersion7() }, message.Payload);
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_delivery WHERE CallId=@id", new { id = call.Id }));
        Assert.AreEqual(message.Id, await db.ExecuteScalarAsync<Guid>("SELECT EnvelopeMessageId FROM fn_workflow_collaboration_delivery WHERE CallId=@id", new { id = call.Id }));
        Guid? targetInstanceId = null;
        if (scenario != "closing")
        {
            await ProtocolCaseAsync(factory, db, source, target, sourceTenant, targetTenant, instance, call.Id, grantId, fault, scenario, before);
            return;
        }
        if (withRelay)
        {
            Task<int> Relay() => RelayAsync(factory, db);
            fault.TargetOrigin = true;
            Assert.AreEqual(0, await Relay()); fault.TargetOrigin = false;
            Assert.IsTrue(fault.TargetHits > 0, "必须实际到达目标来源写入，不能以早期拒绝冒充目标事务回滚。");
            Assert.AreEqual(before + 1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"));
            Assert.IsNull(await db.ExecuteScalarAsync<Guid?>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id }));
            fault.Confirmation = true;
            Assert.AreEqual(0, await Relay()); fault.Confirmation = false;
            Assert.IsTrue(fault.ConfirmationHits > 0);
            targetInstanceId = await db.ExecuteScalarAsync<Guid?>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id });
            Assert.IsNotNull(targetInstanceId, "目标已提交，即使源确认失败也必须保留原目标身份。");
            Assert.AreEqual("requested", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }));
            await ConcurrentRelayAsync(factory); await Relay();
            Assert.AreEqual(before + 2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"));
            Assert.AreEqual(targetInstanceId, await db.ExecuteScalarAsync<Guid?>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id }));
            Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id }));
            Assert.AreEqual("accepted", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }));
            Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = targetInstanceId }));
            Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = instance }));
            Assert.AreEqual(32, await db.ExecuteScalarAsync<int>("SELECT Granted FROM fn_workflow_exec_budget WHERE InstanceId=@id", new { id = targetInstanceId }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT Depth FROM fn_workflow_collaboration_origin WHERE Id=@id AND RootInstanceId=@id", new { id = targetInstanceId }));
            var targetInput = await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@id", new { id = targetInstanceId });
            Assert.AreEqual("{\"title\":\"shared\"}", targetInput);
            using var foreign = await source.GetAsync($"/api/v1/workflow/instances/{targetInstanceId:D}");
            Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
            using var own = await target.GetAsync($"/api/v1/workflow/instances/{targetInstanceId:D}");
            Assert.AreEqual(HttpStatusCode.OK, own.StatusCode, await own.Content.ReadAsStringAsync());
            var todoId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@id AND StatusKey='active'", new { id = targetInstanceId });
            using var todoResponse = await target.GetAsync($"/api/v1/workflow/todos/{todoId:D}");
            Assert.AreEqual(HttpStatusCode.OK, todoResponse.StatusCode, await todoResponse.Content.ReadAsStringAsync());
            var todo = await todoResponse.Content.ReadFromJsonAsync<JsonElement>();
            await PostAsync(target, $"/api/v1/workflow/todos/{todoId:D}/approve", new { expectedRevision = todo.GetProperty("revision").GetInt64(),
                expectedFormRevision = todo.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, comment = "目标方实际办理", idempotencyKey = "collaboration-target-approve" });
            Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = targetInstanceId }));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type AND TenantId " + (targetTenant is null ? "IS NULL" : "= @targetTenant"), new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted, targetTenant }));
            if (sourceTenant is { } closingTenant)
            {
                await using var closing = factory.Services.CreateAsyncScope();
                closing.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new TenantContext(closingTenant, "source", "协作源"));
                var commands = closing.ServiceProvider.GetRequiredService<ICommandExecutor>();
                var closed = await closing.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteResultAsync(async inner =>
                {
                    var parameters = WorkflowSqlParameters.Create(("InstanceId", instance), ("TenantScopeKey", scopeKey), ("Now", DateTimeOffset.UtcNow));
                    await commands.ExecuteAsync(WorkflowExecutionEpochSql.CloseInstance, parameters, inner);
                    Assert.AreEqual(1, await commands.ExecuteAsync(WorkflowCollaborationRelaySql.CloseInvalidated, parameters, inner));
                    return Result<bool>.Success(true);
                }, CancellationToken.None);
                Assert.IsTrue(closed.IsSuccess);
                using var staleEpoch = await target.GetAsync($"/api/v1/workflow/instances/{targetInstanceId:D}");
                Assert.AreEqual(HttpStatusCode.Forbidden, staleEpoch.StatusCode, "源代次关闭后不能再读取旧目标");
            }
            await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/cancel", new { expectedRevision = 1, reason = "关闭源调用", idempotencyKey = "collaboration-source-cancel" });
            Assert.AreEqual("cancel_requested", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id }));
            using var sourceClosed = await target.GetAsync($"/api/v1/workflow/instances/{targetInstanceId:D}");
            Assert.AreEqual(HttpStatusCode.Forbidden, sourceClosed.StatusCode);
            await Relay();
            Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id }));
        }
        await PostAsync(source, $"{grants}/{grantId:D}/revoke", new { expectedRevision = 2, idempotencyKey = "revoke", reason = "请求后撤销不赋予目标执行权" });
        if (targetInstanceId is { } targetId)
        {
            using var closed = await target.GetAsync($"/api/v1/workflow/instances/{targetId:D}");
            Assert.AreEqual(HttpStatusCode.Forbidden, closed.StatusCode, await closed.Content.ReadAsStringAsync());
        }
        await Receive(envelope, message.Payload);
        var revoked = await PostAsync(source, "/api/v1/workflow/instances", Start(Guid.CreateVersion7().ToString("N")), HttpStatusCode.UnprocessableEntity);
        Assert.AreEqual("workflow.transition.invalid", revoked.GetProperty("code").GetString());
        Assert.AreEqual(before + (withRelay ? 2 : 1), await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"));
    }

    private static async Task<Asset> AssetAsync(HttpClient client, bool approval = false, bool files = false, bool claim = false)
    {
        var fields = new List<object> { new { fieldKey = "title", fieldTypeKey = "text", required = false, constraints = new { } }, new { fieldKey = "secret", fieldTypeKey = "text", required = false, constraints = new { } }, new { fieldKey = "answer", fieldTypeKey = "text", required = false, constraints = new { } } };
        if (files)
        {
            foreach (var fieldKey in new[] { "inputFiles", "returnedFiles" }) fields.Add(new { fieldKey, fieldTypeKey = "attachment", required = false, constraints = new { maxCount = 3, maxSizeBytes = 1024, allowedExtensions = new[] { "png" } } });
            foreach (var fieldKey in new[] { "rows", "returnedRows" }) fields.Add(new { fieldKey, fieldTypeKey = "subtable", required = false, constraints = new { maxRows = 4, columns = new object[] {
                new { columnKey = "label", fieldTypeKey = "text", required = true, constraints = new { maxLength = 32 } }, new { columnKey = "amount", fieldTypeKey = "integer", required = true, constraints = new { minimum = 0, maximum = 100 } } } } });
        }
        var form = await PostAsync(client, "/api/v1/workflow/forms", new { formKey = "request." + Guid.CreateVersion7().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1,
            sections = new[] { new { sectionKey = "main", fields } } } }, HttpStatusCode.Created);
        var formId = form.GetProperty("id").GetGuid(); var formVersion = (await PostAsync(client, $"/api/v1/workflow/forms/{formId:D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var key = "request." + Guid.CreateVersion7().ToString("N");
        var nodes = new List<object> { new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1,
            config = JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { approval ? "approve" : claim ? "claim" : "end" } }) } };
        if (approval) nodes.Add(new { nodeKey = "approve", nodeTypeKey = "human.approval", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" } } });
        if (claim) nodes.Add(new { nodeKey = "claim", nodeTypeKey = "human.claim", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" }, claimPolicy = new { allowRelease = true } } });
        nodes.Add(new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = JsonSerializer.SerializeToElement(new { }) });
        var definition = await PostAsync(client, "/api/v1/workflow/definitions", new { definitionKey = key, draft = new { schemaVersion = 1, workflowKindKey = approval ? "approval" : "automation", nodes } }, HttpStatusCode.Created);
        var id = definition.GetProperty("id").GetGuid();
        return new(id, (await PostAsync(client, $"/api/v1/workflow/definitions/{id:D}/publish", new { expectedRevision = 1, formVersionId = formVersion })).GetProperty("id").GetGuid(), formVersion, key);
    }
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body, HttpStatusCode status = HttpStatusCode.OK)
    { using var response = await client.PostAsJsonAsync(path, body); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(status, response.StatusCode, path + ": " + text); return JsonSerializer.Deserialize<JsonElement>(text); }
    private static void Migrate333(DatabaseProvider provider, string connection, int count, bool target = false)
    {
        var builder = provider == DatabaseProvider.SqlServer ? DeployChanges.To.SqlDatabase(connection) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains($".Migrations.{provider}.", StringComparison.Ordinal) && name.EndsWith(target ? ".336_WorkflowCollaborationTargets.sql" : ".333_WorkflowCollaborationRequests.sql", StringComparison.Ordinal))
            .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(count, result.Scripts);
    }
    private sealed record Asset(Guid Definition, Guid Version, Guid Form, string Key);
    private sealed record Message(Guid Id, string MessageType, int SchemaVersion, Guid? TenantId, string? TraceId, DateTimeOffset OccurredAtUtc, byte[] Payload);
    private sealed class InsertFault
    {
        public bool Enabled { get; set; } public int Hits { get; set; }
        public bool TargetOrigin { get; set; } public int TargetHits { get; set; }
        public bool Confirmation { get; set; } public int ConfirmationHits { get; set; }
        public bool Completion { get; set; } public int CompletionHits { get; set; }
        public bool Freeze { get; set; } public int FreezeHits { get; set; }
    }
    private sealed class FaultExecutor(ICommandExecutor inner, InsertFault fault) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (fault.Enabled && statement == WorkflowCollaborationCallSql.Insert) { fault.Hits++; return Task.FromResult(0); }
            if (fault.TargetOrigin && statement == WorkflowCollaborationRelaySql.InsertOrigin) { fault.TargetHits++; return Task.FromResult(0); }
            if (fault.Confirmation && statement == WorkflowCollaborationRelaySql.ConfirmAccepted) { fault.ConfirmationHits++; return Task.FromResult(0); }
            if (fault.Completion && statement == WorkflowCollaborationControlSql.Apply && parameters is Dictionary<string, object?> values && values.GetValueOrDefault("KindKey") is "completed") { fault.CompletionHits++; return Task.FromResult(0); }
            if (fault.Freeze && statement == WorkflowCollaborationFileSql.Freeze) { fault.FreezeHits++; return Task.FromResult(0); }
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}
