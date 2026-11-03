extern alias workerhost;

using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RealOutboxProcessor = workerhost::Full.NET.Host.Worker.OutboxProcessor;
using RealOutboxOptions = workerhost::Full.NET.Host.Worker.OutboxWorkerOptions;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod]
    public async Task Collaboration_worker_delivery_with_sql_server() =>
        await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), true, withWorker: true);
    [TestMethod]
    public async Task Collaboration_worker_delivery_with_mysql() =>
        await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), true, withWorker: true);
    [TestMethod]
    public async Task Collaboration_worker_children_with_sql_server() =>
        await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), true, withWorker: true, withChildren: true);
    [TestMethod]
    public async Task Collaboration_worker_children_with_mysql() =>
        await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), true, withWorker: true, withChildren: true);

    private static async Task<Asset> ChildAssetAsync(HttpClient target, bool files = false, int depth = 1, bool parallel = false)
    {
        var child = await AssetAsync(target, approval: true, files: files);
        for (var level = 0; level < depth; level++) child = await WrapChildAssetAsync(target, child, files, parallel);
        return child;
    }

    private static async Task<Asset> WrapChildAssetAsync(HttpClient target, Asset child, bool files, bool parallel)
    {
        var key = "collab.child." + Guid.CreateVersion7().ToString("N");
        var input = new Dictionary<string, string> { ["title"] = "title" };
        var output = new Dictionary<string, string> { ["title"] = "title" };
        var policies = new Dictionary<string, string> { ["title"] = "editable" };
        if (files)
        {
            input["inputFiles"] = "inputFiles"; input["rows"] = "rows";
            output["returnedFiles"] = "returnedFiles"; output["rows"] = "rows";
            policies["inputFiles"] = "readOnly"; policies["rows"] = "editable"; policies["returnedFiles"] = "editable";
        }
        var config = new Dictionary<string, object> { ["nextNodeKeys"] = new[] { "end" }, ["fieldPolicies"] = policies,
            ["subprocessPolicy"] = new { definitionVersionId = child.Version, inputFields = input, outputFields = output } };
        if (parallel) config["multiInstancePolicy"] = new { modeKey = "parallel", source = new { kindKey = "fixed_count", count = 2 },
            requiredPercentage = 100, emptyModeKey = "reject", outputReducers = output.Keys.ToDictionary(field => field,
                field => field is "returnedFiles" or "rows" ? "append" : "first", StringComparer.Ordinal) };
        var definition = await PostAsync(target, "/api/v1/workflow/definitions", new { definitionKey = key, draft = new {
            schemaVersion = 1, workflowKindKey = "automation", nodes = new object[] {
                new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "child" } } },
                new { nodeKey = "child", nodeTypeKey = "process.subprocess", nodeSchemaVersion = 1, config },
                new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { } } } } }, HttpStatusCode.Created);
        var id = definition.GetProperty("id").GetGuid();
        var published = await PostAsync(target, $"/api/v1/workflow/definitions/{id:D}/publish", new { expectedRevision = 1, formVersionId = child.Form });
        return new(id, published.GetProperty("id").GetGuid(), child.Form, key);
    }

    /// <summary>独立Worker宿主只保留生产Outbox/协作/恢复循环；不直接调用Handler，也不据此声称Kafka物理链路。</summary>
    private sealed partial class WorkerDeliveryFlow(bool withChildren = false, bool withFiles = false, WorkerTreeShape? treeShape = null, bool publicSource = false,
        PublicNodeAuthoringBrowser? authoringBrowser = null)
    {
        internal bool WithChildren => withChildren || treeShape is not null;
        internal bool WithFiles => withFiles;
        internal WorkerTreeShape? TreeShape => treeShape;
        internal bool PublicSource => publicSource;
        internal PublicNodeAuthoringBrowser? AuthoringBrowser => authoringBrowser;
        internal int OfferedBudget => treeShape is not null ? 256 : WithChildren ? 128 : 64;
        internal int GrantedBudget(string scenario) => WithChildren && scenario != "worker_budget_denied" ? OfferedBudget : 32;
        private static Task<IHost> BuildAsync(FullNetApiFactory factory) => IntegrationWorkerHostFactory.BuildAsync(factory.Provider,
            factory.ConnectionString, new Dictionary<string, string?> { ["Workflow:RecoveryWorker:PollMilliseconds"] = "100",
                ["Workflow:RecoveryWorker:ScanIntervalSeconds"] = "1", ["Workflow:RecoveryWorker:BatchSize"] = "10",
                ["Logging:LogLevel:Full.NET.Modules.Workflow.Execution.WorkflowCollaborationRelay"] = "Debug" },
            "Full.NET.IntegrationTests.WorkflowCollaboration.Worker", nameof(WorkflowCollaborationHostedProcessor),
            fileRootPath: factory.Services.GetRequiredService<IConfiguration>()["Files:Local:RootPath"] ?? throw new InvalidOperationException("The actual API Files root is required."),
            additionalHostedServiceNames: [typeof(RealOutboxProcessor).Name, nameof(WorkflowRecoveryHostedProcessor), nameof(WorkflowSubprocessHostedProcessor)], configureServices: services =>
            {
                services.AddOptions<RealOutboxOptions>().Configure(options =>
                {
                    options.PollMilliseconds = 100; options.MaximumIdlePollMilliseconds = 1000;
                    options.BatchSize = 20; options.MaxConcurrency = 2; options.LeaseSeconds = 10; options.LeaseRenewalSeconds = 3;
                });
                services.AddHostedService<RealOutboxProcessor>();
            });

        private static async Task UntilAsync(Func<Task<bool>> condition, string message)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
            while (!await condition().ConfigureAwait(false))
            {
                if (DateTimeOffset.UtcNow >= deadline) Assert.Fail(message);
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        private static async Task<Guid> QueueStuckTaskAsync(DbConnection db, Guid instance)
        {
            var task = Guid.CreateVersion7();
            Assert.AreEqual(1, await db.ExecuteAsync("""
                INSERT INTO fn_workflow_recovery_task
                    (Id,TenantId,ScopeKey,TenantScopeKey,InstanceId,StepId,KindKey,StatusKey,AttemptCount,Revision,LeaseOwnerKey,LeaseExpiresAtUtc,LeaseGeneration,NextAttemptAtUtc,LastError,CreatedAtUtc,UpdatedAtUtc)
                SELECT @task,TenantId,ScopeKey,TenantScopeKey,Id,NULL,'stuck_instance','pending',0,1,NULL,NULL,0,NULL,NULL,@now,@now
                FROM fn_workflow_instance WHERE Id=@instance
                """, new { task, instance, now = DateTimeOffset.UtcNow }));
            return task;
        }

        private static async Task VerifyWaitingTaskAsync(DbConnection db, Guid instance)
        {
            // 先前扫描排队的事实仍应被生产循环安全关闭，不能只修新扫描而留下会误暂停的旧任务。
            var task = await QueueStuckTaskAsync(db, instance);
            await UntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_recovery_task WHERE Id=@task", new { task }) == "succeeded",
                "真实活动协作/子流程等待必须关闭卡住任务，不能重试至暂停。");
            Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT AttemptCount FROM fn_workflow_recovery_task WHERE Id=@task", new { task }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE ResourceId=@task AND ActorUserId IS NULL AND OutcomeKey='succeeded'", new { task }));
        }

        internal async Task RunAsync(FullNetApiFactory factory, DbConnection db, HttpClient source, HttpClient target,
            Guid instance, WorkflowCollaborationCallRecord call, Guid grant, string scenario, int before, Guid? originalFile = null)
        {
            if (TreeShape is not null && scenario != "worker_budget_denied")
            {
                await RunTreeAsync(factory, db, source, target, instance, call, grant, scenario, before, originalFile!.Value);
                return;
            }
            Message? message = null;
            if (WithFiles) Assert.AreEqual("preparing", call.StatusKey, "文件必须由生产循环准备，测试不能预先驱动准备器。");
            var completedBefore = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted });
            var cancelledBefore = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCancelled });
            if (scenario == "worker_budget_denied")
            {
                using var deniedHost = await BuildAsync(factory); await deniedHost.StartAsync();
                try
                {
                    await UntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }) == "denied",
                        "固定子树超出同意切片必须明确拒绝，不能无限重试或提高预算。");
                    await UntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_control AS control INNER JOIN fn_outbox_message AS message ON message.Id=control.EnvelopeMessageId WHERE control.CallId=@id AND control.KindKey='cancelled' AND control.StatusKey='applied' AND message.ProcessedAtUtc IS NOT NULL", new { id = call.Id }) == 1,
                        "拒绝也必须通过真实Outbox关闭确认并完成接纳。");
                    Assert.AreEqual("suspended", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
                    Assert.IsNull(await db.ExecuteScalarAsync<Guid?>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id }));
                    Assert.AreEqual(before + 1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"), "失败启动必须回滚所有目标及子根。");
                    Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }));
                    Assert.AreEqual(completedBefore, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted }));
                }
                finally { await deniedHost.StopAsync(); }
                await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke", new { expectedRevision = 2, idempotencyKey = "worker-budget-revoke", reason = "预算拒绝场景结束" });
                if (WithFiles)
                {
                    Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_read_capability WHERE CallId=@id AND DirectionKey='input'", new { id = call.Id }));
                    await CleanupFileCapabilitiesAsync(factory, db, call.Id);
                    await BytesAsync(source, instance, originalFile!.Value, FileBytes);
                }
                return;
            }
            Guid? childId = null;
            using (var first = await BuildAsync(factory))
            {
                await first.StartAsync();
                try
                {
                    await UntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }) == "accepted",
                        "生产Outbox和Relay必须真实交付创建确认。");
                    message = await db.QuerySingleAsync<Message>("""
                        SELECT message.Id,message.MessageType,message.SchemaVersion,message.TenantId,message.TraceId,message.OccurredAtUtc,message.Payload
                        FROM fn_outbox_message AS message INNER JOIN fn_workflow_collaboration_delivery AS delivery ON delivery.EnvelopeMessageId=message.Id
                        WHERE delivery.CallId=@id
                        """, new { id = call.Id });
                    await UntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id=@id AND ProcessedAtUtc IS NOT NULL", new { id = message.Id }) == 1,
                        "真实Outbox领取必须完成原请求，不能用手写收件代替。");
                    var scan = factory.Provider == DatabaseProvider.SqlServer ? WorkflowRecoverySql.ScanStuckInstancesSqlServer : WorkflowRecoverySql.ScanStuckInstancesMySql;
                    var candidates = await db.QueryAsync<WorkflowRecoveryScanCandidate>(scan.Text,
                        new { Now = DateTimeOffset.UtcNow.AddDays(1), KindKey = WorkflowRecoveryKinds.StuckInstance, BatchSize = 50 });
                    Assert.IsFalse(candidates.Any(candidate => candidate.InstanceId == instance), "真实协作等待不能当作缺少待办的卡住实例。");
                    Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_recovery_task WHERE InstanceId=@instance", new { instance }),
                        "生产扫描循环不能把协作等待转为重试或死信。");
                    await VerifyWaitingTaskAsync(db, instance);
                    if (WithChildren)
                    {
                        await UntilAsync(async () => await db.ExecuteScalarAsync<Guid?>("SELECT item.ChildInstanceId FROM fn_workflow_subprocess_item AS item INNER JOIN fn_workflow_collaboration_admission AS admission ON admission.TargetInstanceId=item.InstanceId WHERE admission.Id=@call", new { call = call.Id }) is { } id && id != Guid.Empty,
                            "独立Worker必须从真实目标根创建子实例。");
                        childId = await db.ExecuteScalarAsync<Guid>("SELECT item.ChildInstanceId FROM fn_workflow_subprocess_item AS item INNER JOIN fn_workflow_collaboration_admission AS admission ON admission.TargetInstanceId=item.InstanceId WHERE admission.Id=@call", new { call = call.Id });
                        var root = await db.ExecuteScalarAsync<Guid>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@call", new { call = call.Id });
                        var origin = await db.QuerySingleAsync<WorkflowCollaborationOriginRecord>("SELECT * FROM fn_workflow_collaboration_origin WHERE Id=@childId", new { childId });
                        Assert.AreEqual(call.Id, origin.CallId); Assert.AreEqual(root, origin.RootInstanceId);
                        var rootDepth = await db.ExecuteScalarAsync<int>("SELECT Depth FROM fn_workflow_collaboration_origin WHERE Id=@root", new { root });
                        Assert.AreEqual(rootDepth + 1, origin.Depth);
                        var candidatesWithChild = await db.QueryAsync<WorkflowRecoveryScanCandidate>(scan.Text,
                            new { Now = DateTimeOffset.UtcNow.AddDays(1), KindKey = WorkflowRecoveryKinds.StuckInstance, BatchSize = 50 });
                        Assert.IsFalse(candidatesWithChild.Any(candidate => candidate.InstanceId == root), "真实子流程等待不能被卡住扫描误选。");
                        await VerifyWaitingTaskAsync(db, root);
                        var parentBudget = await db.ExecuteScalarAsync<int>("SELECT Granted FROM fn_workflow_exec_budget WHERE InstanceId=@root", new { root });
                        var childBudget = await db.ExecuteScalarAsync<int>("SELECT Granted FROM fn_workflow_exec_budget WHERE InstanceId=@childId", new { childId });
                        Assert.IsLessThan(parentBudget, childBudget); Assert.AreEqual(call.ReservedActivities, parentBudget);
                        using var detail = await target.GetAsync($"/api/v1/workflow/instances/{childId:D}");
                        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode, await detail.Content.ReadAsStringAsync());
                    }
                }
                finally { await first.StopAsync(); }
            }
            var targetId = await db.ExecuteScalarAsync<Guid>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id });
            if (message is null) throw new InvalidOperationException("The real requested envelope was not recorded.");
            WorkerFileFacts? fileFacts = WithFiles ? await ReadWorkerFilesAsync(db, source, target, instance, childId ?? targetId, call.Id, originalFile!.Value) : null;
            Assert.AreEqual(before + (WithChildren ? 3 : 2), await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"));
            Assert.AreEqual(message.Id, await db.ExecuteScalarAsync<Guid>("SELECT EnvelopeMessageId FROM fn_workflow_collaboration_delivery WHERE CallId=@id", new { id = call.Id }));
            if (scenario == "worker_cancel")
            {
                await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/cancel", new { expectedRevision = 1, reason = "投递进程离线期间取消", idempotencyKey = "worker-cancel" });
                using var denied = await target.GetAsync($"/api/v1/workflow/instances/{targetId:D}"); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
                if (childId is { } closedChild)
                {
                    using var deniedChild = await target.GetAsync($"/api/v1/workflow/instances/{closedChild:D}"); Assert.AreEqual(HttpStatusCode.Forbidden, deniedChild.StatusCode);
                }
                Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@targetId", new { targetId }));
            }
            else
            {
                var todoId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@targetId AND StatusKey='active'", new { targetId = childId ?? targetId });
                var todo = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}");
                object patch = new { title = "worker result" };
                if (fileFacts is { } currentFiles)
                {
                    var reply = await PrepareFileReplyAsync(target, todoId, todo, currentFiles);
                    patch = reply.Patch; fileFacts = reply.Files;
                }
                await PostAsync(target, $"/api/v1/workflow/todos/{todoId:D}/approve", new { expectedRevision = todo.GetProperty("revision").GetInt64(),
                    expectedFormRevision = todo.GetProperty("submissionRevision").GetInt64(), fieldPatch = patch, comment = "生产投递", idempotencyKey = "worker-result" });
                Assert.AreEqual("accepted", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }), "Worker离线时不能伪造源接纳。");
            }
            using (var restarted = await BuildAsync(factory))
            {
                await restarted.StartAsync();
                try
                {
                    var expected = scenario == "worker_cancel" ? "cancelled" : "completed";
                    await UntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }) == expected,
                        "重启必须从耐久请求、真实目标终态与Outbox恢复结果/关闭确认。");
                    await UntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }) == expected,
                        "真实恢复循环必须推进源最终状态。");
                    if (childId is { } child)
                    {
                        await UntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@child", new { child }) == expected,
                            "最终结果或取消必须贯穿真实子根，不能遗留活动后代。");
                    }
                    var kind = scenario == "worker_cancel" ? "cancelled" : "completed";
                    await UntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_control WHERE CallId=@id AND KindKey=@kind AND StatusKey='applied'", new { id = call.Id, kind }) == 1,
                        "源必须接纳真实控制收件。");
                    await UntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_control AS control INNER JOIN fn_outbox_message AS message ON message.Id=control.EnvelopeMessageId WHERE control.CallId=@id AND control.KindKey=@kind AND message.ProcessedAtUtc IS NOT NULL", new { id = call.Id, kind }) == 1,
                        "控制收件必须来自实际Outbox信封且已完成消费确认。");
                    // 模拟终态提交后遗留的真实恢复任务，由运行中的生产循环关闭并提交系统审计。
                    var task = await QueueStuckTaskAsync(db, instance);
                    await UntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE ResourceId=@task AND ActorUserId IS NULL AND OperationKey='recovery.stuck_instance' AND OutcomeKey='succeeded'", new { task }) == 1,
                        "恢复循环必须以真实租约关闭遗留任务并写入受约束的系统审计。");
                    var detail = await db.ExecuteScalarAsync<string>("SELECT DetailJson FROM fn_workflow_domain_audit WHERE ResourceId=@task", new { task });
                    using var audit = JsonDocument.Parse(detail!);
                    Assert.AreEqual(task, audit.RootElement.GetProperty("taskId").GetGuid());
                    Assert.AreEqual(instance, audit.RootElement.GetProperty("instanceId").GetGuid());
                    Assert.AreEqual(1, audit.RootElement.GetProperty("attempt").GetInt32());
                    Assert.AreEqual(await db.ExecuteScalarAsync<int>("SELECT LeaseGeneration FROM fn_workflow_recovery_task WHERE Id=@task", new { task }), audit.RootElement.GetProperty("leaseGeneration").GetInt32());
                }
                finally { await restarted.StopAsync(); }
            }
            var values = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!);
            Assert.AreEqual("source-private", values.GetProperty("secret").GetString());
            if (scenario == "worker_success") Assert.AreEqual("worker result", values.GetProperty("answer").GetString());
            else Assert.IsFalse(values.TryGetProperty("answer", out _));
            if (fileFacts is { } completedFiles) await VerifyWorkerFileResultAsync(db, source, target, instance, completedFiles, values, scenario);
            var targetUsed = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step AS step INNER JOIN fn_workflow_collaboration_origin AS origin ON origin.Id=step.InstanceId WHERE origin.RootInstanceId=@targetId", new { targetId });
            Assert.AreEqual(targetUsed, await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }), "源只保留真实根/子步骤消耗，不能重复归还或放大切片。");
            Assert.AreEqual(completedBefore + (scenario == "worker_success" ? 1 : 0), await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCompleted }),
                "目标协作根不能重复发布原业务完成提醒。");
            Assert.AreEqual(cancelledBefore + (scenario == "worker_cancel" ? 1 : 0), await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = WorkflowNotificationIntegrationEventTypes.InstanceCancelled }),
                "目标协作根不能重复发布原业务取消提醒。");
            Assert.AreEqual(before + (WithChildren ? 3 : 2), await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"), "重启不得另建目标或子根。");
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_delivery WHERE CallId=@id", new { id = call.Id }));
            await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke", new { expectedRevision = 2, idempotencyKey = "worker-end-revoke", reason = "独立Worker验收结束" });
            if (fileFacts is { } revokedFiles) await VerifyWorkerFileRevocationAsync(factory, db, source, target, instance, revokedFiles, values, call.Id, scenario);
        }
    }

    private static async Task RecoverWorkerAuditAsync(DatabaseProvider provider, string connection, DbConnection db)
    {
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit");
        var taskCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_recovery_task");
        Assert.IsGreaterThanOrEqualTo(4, taskCount);
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE fn_workflow_domain_audit DROP CONSTRAINT ck_fn_workflow_domain_audit_actor; DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%341_WorkflowRecoverySystemAudit.sql'"
            : "ALTER TABLE fn_workflow_domain_audit DROP CHECK ck_fn_workflow_domain_audit_actor; DELETE FROM schemaversions WHERE ScriptName LIKE '%341_WorkflowRecoverySystemAudit.sql'");
        foreach (var expected in new[] { 1, 0 })
        {
            var builder = provider == DatabaseProvider.SqlServer ? DeployChanges.To.SqlDatabase(connection) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains($".Migrations.{provider}.", StringComparison.Ordinal) && name.EndsWith(".341_WorkflowRecoverySystemAudit.sql", StringComparison.Ordinal))
                .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(expected, result.Scripts);
        }
        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit"));
        Assert.AreEqual(taskCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_recovery_task"));
        var row = await db.QueryFirstAsync<RecoveryAuditFixture>("SELECT ResourceId,InstanceId,DetailJson FROM fn_workflow_domain_audit WHERE OperationKey='recovery.stuck_instance'");
        var good = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.DetailJson)!;
        foreach (var defect in new[] { "manual", "marker", "task", "instance", "null_instance", "invalid_guid", "kind", "lease", "attempt" })
        {
            var invalid = new Dictionary<string, JsonElement>(good);
            switch (defect)
            {
                case "marker": invalid.Remove("sourceKey"); break;
                case "task": invalid["taskId"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()); break;
                case "instance": invalid["instanceId"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()); break;
                case "invalid_guid": invalid["taskId"] = JsonSerializer.SerializeToElement("invalid"); break;
                case "kind": invalid["kindKey"] = JsonSerializer.SerializeToElement("expired_lease"); break;
                case "lease": invalid["leaseGeneration"] = JsonSerializer.SerializeToElement(0); break;
                case "attempt": invalid["attempt"] = JsonSerializer.SerializeToElement(0); break;
            }
            try
            {
                await db.ExecuteAsync("""
                    INSERT INTO fn_workflow_domain_audit
                        (Id,TenantId,ScopeKey,InstanceId,OperationKey,ActorUserId,ResourceTypeKey,ResourceId,OutcomeKey,DetailJson,CreatedAtUtc,CorrelationJson)
                    SELECT @id,TenantId,ScopeKey,@instanceId,@operation,NULL,@resource,ResourceId,'succeeded',@detail,@now,CorrelationJson
                    FROM fn_workflow_domain_audit WHERE ResourceId=@task
                    """, new { id = Guid.CreateVersion7(), operation = defect == "manual" ? "instance.pause" : "recovery.stuck_instance",
                        resource = defect == "manual" ? "instance" : "recovery_task", instanceId = defect == "null_instance" ? (Guid?)null : row.InstanceId,
                        detail = JsonSerializer.Serialize(invalid), now = DateTimeOffset.UtcNow, task = row.ResourceId });
                Assert.Fail("审计约束必须拒绝缺失或伪造的系统身份：" + defect);
            }
            catch (DbException exception) when ((exception is Microsoft.Data.SqlClient.SqlException { Number: 547 }) ||
                (exception is MySqlConnector.MySqlException { Number: 3819 })) { }
        }
        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit"));
    }

    private sealed record RecoveryAuditFixture(Guid ResourceId, Guid InstanceId, string DetailJson);
}
