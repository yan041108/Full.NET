using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    private enum WorkerTreeShape { Deep, Parallel }

    [TestMethod]
    public async Task Collaboration_worker_deep_files_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer,
        await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), withWorker: true, withFiles: true, treeShape: WorkerTreeShape.Deep);
    [TestMethod]
    public async Task Collaboration_worker_deep_files_with_mysql() => await VerifyAsync(DatabaseProvider.MySql,
        await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), withWorker: true, withFiles: true, treeShape: WorkerTreeShape.Deep);
    [TestMethod]
    public async Task Collaboration_worker_parallel_files_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer,
        await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), withWorker: true, withFiles: true, treeShape: WorkerTreeShape.Parallel);
    [TestMethod]
    public async Task Collaboration_worker_parallel_files_with_mysql() => await VerifyAsync(DatabaseProvider.MySql,
        await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), withWorker: true, withFiles: true, treeShape: WorkerTreeShape.Parallel);

    private sealed partial class WorkerDeliveryFlow
    {
        private sealed record TreeOrigin(Guid Id, Guid RootInstanceId, Guid CallId, int Depth);

        /// <summary>真实生产循环创建两层子代或两个并行子根；停机后办理/取消/撤权，再由耐久事实恢复整树。</summary>
        private async Task RunTreeAsync(FullNetApiFactory factory, DbConnection db, HttpClient source, HttpClient target,
            Guid instance, WorkflowCollaborationCallRecord call, Guid grant, string scenario, int before, Guid originalFile)
        {
            Assert.AreEqual("preparing", call.StatusKey);
            var completedBefore = await NotificationCountAsync(db, WorkflowNotificationIntegrationEventTypes.InstanceCompleted);
            var cancelledBefore = await NotificationCountAsync(db, WorkflowNotificationIntegrationEventTypes.InstanceCancelled);
            var leafCount = TreeShape == WorkerTreeShape.Parallel ? 2 : 1;
            Guid targetId = default; TreeOrigin[] origins = []; Guid[] leaves = [];
            using (var first = await BuildAsync(factory))
            {
                await first.StartAsync();
                try
                {
                    await UntilAsync(async () => await db.ExecuteScalarAsync<string>(
                        "SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }) == "accepted", "真实目标创建确认必须被源接纳。");
                    targetId = await db.ExecuteScalarAsync<Guid>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id });
                    await UntilAsync(async () => await db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM fn_workflow_collaboration_origin WHERE RootInstanceId=@targetId", new { targetId }) == 3, "两层子代或两个并行子根必须全部真实创建。");
                    origins = (await db.QueryAsync<TreeOrigin>("SELECT Id,RootInstanceId,CallId,Depth FROM fn_workflow_collaboration_origin WHERE RootInstanceId=@targetId", new { targetId })).ToArray();
                    Assert.HasCount(3, origins); Assert.IsTrue(origins.All(row => row.CallId == call.Id && row.RootInstanceId == targetId));
                    var rootDepth = origins.Single(row => row.Id == targetId).Depth;
                    CollectionAssert.AreEquivalent(TreeShape == WorkerTreeShape.Deep ? new[] { rootDepth, rootDepth + 1, rootDepth + 2 }
                        : new[] { rootDepth, rootDepth + 1, rootDepth + 1 }, origins.Select(row => row.Depth).ToArray());
                    var ids = origins.Select(row => row.Id).ToArray();
                    await UntilAsync(async () => await db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId IN @ids AND StatusKey='active'", new { ids }) == leafCount, "每个叶子必须有真实人工待办。");
                    leaves = (await db.QueryAsync<Guid>("SELECT InstanceId FROM fn_workflow_todo WHERE InstanceId IN @ids AND StatusKey='active' ORDER BY InstanceId", new { ids })).ToArray();
                    Assert.HasCount(leafCount, leaves); Assert.HasCount(leafCount, leaves.Distinct().ToArray());
                    Assert.AreEqual(256, await db.ExecuteScalarAsync<int>("SELECT Granted FROM fn_workflow_exec_budget WHERE InstanceId=@targetId", new { targetId }));
                    foreach (var row in origins)
                    {
                        using var detail = await target.GetAsync($"/api/v1/workflow/instances/{row.Id:D}");
                        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode, await detail.Content.ReadAsStringAsync());
                        if (!leaves.Contains(row.Id)) await VerifyWaitingTaskAsync(db, row.Id);
                    }
                    var slices = await db.QueryAsync<(int ParentGranted, int ChildGranted)>("""
                        SELECT parentBudget.Granted AS ParentGranted,childBudget.Granted AS ChildGranted
                        FROM fn_workflow_subprocess_item AS item
                        INNER JOIN fn_workflow_exec_budget AS parentBudget ON parentBudget.InstanceId=item.InstanceId
                        INNER JOIN fn_workflow_exec_budget AS childBudget ON childBudget.InstanceId=item.ChildInstanceId
                        WHERE item.InstanceId IN @ids
                        """, new { ids });
                    Assert.HasCount(2, slices.ToArray()); Assert.IsTrue(slices.All(slice => slice.ChildGranted < slice.ParentGranted));
                    Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("""
                        SELECT COUNT(*) FROM fn_workflow_collaboration_delivery AS delivery
                        INNER JOIN fn_outbox_message AS message ON message.Id=delivery.EnvelopeMessageId
                        WHERE delivery.CallId=@id AND message.ProcessedAtUtc IS NOT NULL
                        """, new { id = call.Id }), "不能以手工收件代替生产Outbox消费。");
                }
                finally { await first.StopAsync(); }
            }
            Assert.AreEqual(before + 4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"));
            var facts = new List<WorkerFileFacts>(); var uploaded = new List<Guid>();
            foreach (var leaf in leaves) facts.Add(await ReadWorkerFilesAsync(db, source, target, instance, leaf, call.Id, originalFile));
            Assert.AreEqual(1, facts.Select(file => file.InputCapability).Distinct().Count(), "父子与并行投影不能重复签发同一输入能力。");
            var success = scenario == "worker_success";
            if (success)
            {
                for (var index = 0; index < leaves.Length; index++)
                {
                    var todoId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@leaf AND StatusKey='active'", new { leaf = leaves[index] });
                    var todo = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}");
                    var file = await UploadViaWorkflowAsync(target, $"/api/v1/workflow/todos/{todoId:D}/attachment-uploads?expectedRevision={todo.GetProperty("revision").GetInt64()}&fieldKey=returnedFiles",
                        "tree-target.png", WorkerTargetBytes); uploaded.Add(file);
                    // 首项回传原输入，其余项仅追加自己的文件，避免在显式append中构造重复附件值。
                    Guid[] returned = index == 0 ? [facts[index].InputCapability, file] : [file];
                    await PostAsync(target, $"/api/v1/workflow/todos/{todoId:D}/approve", new { expectedRevision = todo.GetProperty("revision").GetInt64(),
                        expectedFormRevision = todo.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { title = "worker result", returnedFiles = returned,
                            secret = "target-private", rows = new[] { new { label = "tree row " + index, amount = index + 1 } } },
                        comment = "真实组合叶子办理", idempotencyKey = "tree-result-" + index });
                }
                Assert.AreEqual("accepted", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }), "停机期间不能伪造父子收敛或源接纳。");
            }
            else
            {
                if (scenario == "worker_cancel") await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/cancel",
                    new { expectedRevision = 1, reason = "整树离线取消", idempotencyKey = "tree-cancel" });
                else await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke",
                    new { expectedRevision = 2, reason = "整树离线撤权", idempotencyKey = "tree-revoke" });
                foreach (var row in origins)
                {
                    using var denied = await target.GetAsync($"/api/v1/workflow/instances/{row.Id:D}"); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
                }
                foreach (var file in facts)
                {
                    using var denied = await target.GetAsync(ContentPath(file.ActingInstance, file.InputCapability)); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
                }
            }
            using (var restarted = await BuildAsync(factory))
            {
                await restarted.StartAsync();
                try
                {
                    var callStatus = success ? "completed" : scenario == "worker_revoked" ? "denied" : "cancelled";
                    var sourceStatus = scenario == "worker_revoked" ? "suspended" : callStatus;
                    var targetStatus = success ? "completed" : "cancelled"; var ids = origins.Select(row => row.Id).ToArray();
                    await UntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }) == callStatus, "真实重启必须接纳整树结果或关闭确认。");
                    await UntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }) == sourceStatus, "源必须按真实结果收敛，撤权不能伪造业务成功。");
                    await UntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE Id IN @ids AND StatusKey=@targetStatus", new { ids, targetStatus }) == 3, "整树不得残留活动或暂停后代。");
                    await UntilAsync(async () => await db.ExecuteScalarAsync<int>("""
                        SELECT COUNT(*) FROM fn_workflow_collaboration_control AS control INNER JOIN fn_outbox_message AS message ON message.Id=control.EnvelopeMessageId
                        WHERE control.CallId=@id AND control.KindKey=@kind AND control.StatusKey='applied' AND message.ProcessedAtUtc IS NOT NULL
                        """, new { id = call.Id, kind = success ? "completed" : "cancelled" }) == 1, "最终控制必须有唯一真实信封与消费确认。");
                }
                finally { await restarted.StopAsync(); }
            }
            using var json = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!);
            var values = json.RootElement; Assert.AreEqual("source-private", values.GetProperty("secret").GetString());
            if (success)
            {
                Assert.AreEqual("worker result", values.GetProperty("answer").GetString());
                var returned = values.GetProperty("returnedFiles").EnumerateArray().Select(file => file.GetGuid()).ToArray();
                Assert.HasCount(leafCount + 1, returned); Assert.HasCount(leafCount + 1, returned.Distinct().ToArray());
                Assert.IsTrue(returned.Contains(originalFile)); Assert.IsFalse(returned.Any(uploaded.Contains));
                foreach (var file in returned.Where(file => file != originalFile)) await BytesAsync(source, instance, file, WorkerTargetBytes);
                CollectionAssert.AreEquivalent(Enumerable.Range(0, leafCount).Select(index => "tree row " + index).ToArray(),
                    values.GetProperty("returnedRows").EnumerateArray().Select(row => row.GetProperty("label").GetString()!).ToArray());
            }
            else Assert.IsFalse(values.TryGetProperty("answer", out _));
            var capabilities = (await db.QueryAsync<Guid>("SELECT Id FROM fn_files_read_capability WHERE CallId=@id", new { id = call.Id })).ToArray();
            Assert.HasCount(success ? leafCount + 1 : 1, capabilities, "重复生产轮询不能重复签发输入或结果能力。");
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId IN @capabilities",
                new { capabilities }), "所有子代都不得为跨作用域能力创建伪造本地文件claim。");
            Assert.AreEqual(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step AS step INNER JOIN fn_workflow_collaboration_origin AS origin ON origin.Id=step.InstanceId WHERE origin.RootInstanceId=@targetId", new { targetId }),
                await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }), "源额度必须恰等于整树实际消耗。");
            Assert.AreEqual(before + 4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"));
            Assert.AreEqual(completedBefore + (success ? 1 : 0), await NotificationCountAsync(db, WorkflowNotificationIntegrationEventTypes.InstanceCompleted));
            Assert.AreEqual(cancelledBefore + (scenario == "worker_cancel" ? 1 : 0), await NotificationCountAsync(db, WorkflowNotificationIntegrationEventTypes.InstanceCancelled));
            if (scenario != "worker_revoked") await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke",
                new { expectedRevision = 2, reason = "整树验收结束", idempotencyKey = "tree-end" });
            if (success) foreach (var file in values.GetProperty("returnedFiles").EnumerateArray().Select(file => file.GetGuid()).Where(file => file != originalFile))
            {
                using var denied = await source.GetAsync(ContentPath(instance, file)); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            await CleanupFileCapabilitiesAsync(factory, db, call.Id); await BytesAsync(source, instance, originalFile, FileBytes);
        }

        private static Task<int> NotificationCountAsync(DbConnection db, string type) =>
            db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type });
    }
}
