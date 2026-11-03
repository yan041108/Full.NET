using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod]
    public async Task Collaboration_worker_files_with_sql_server() =>
        await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), true, withFiles: true, withWorker: true);
    [TestMethod]
    public async Task Collaboration_worker_files_with_mysql() =>
        await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), true, withFiles: true, withWorker: true);
    [TestMethod]
    public async Task Collaboration_worker_child_files_with_sql_server() =>
        await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), true, withFiles: true, withWorker: true, withChildren: true);
    [TestMethod]
    public async Task Collaboration_worker_child_files_with_mysql() =>
        await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), true, withFiles: true, withWorker: true, withChildren: true);

    /// <summary>真实生产循环完成文件准备/协议投递，测试只通过API上传与读取，不手驱动处理器。</summary>
    private sealed partial class WorkerDeliveryFlow
    {
        private sealed record WorkerFileFacts(Guid SourceFile, Guid InputCapability, Guid ActingInstance, Guid? TargetFile = null);
        private static readonly byte[] WorkerTargetBytes = FileBytes.Concat(new byte[] { 23 }).ToArray();

        private static async Task<WorkerFileFacts> ReadWorkerFilesAsync(DbConnection db, HttpClient source, HttpClient target,
            Guid sourceInstance, Guid actingInstance, Guid call, Guid originalFile)
        {
            var projection = await db.ExecuteScalarAsync<string>("SELECT ProjectionJson FROM fn_workflow_collaboration_admission WHERE Id=@call", new { call });
            Assert.IsFalse(projection!.Contains(originalFile.ToString("D"), StringComparison.Ordinal));
            Assert.IsFalse(projection.Contains("source-private", StringComparison.Ordinal));
            using var input = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@actingInstance", new { actingInstance }))!);
            var capability = input.RootElement.GetProperty("inputFiles")[0].GetGuid(); Assert.AreNotEqual(originalFile, capability);
            var todoId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@actingInstance AND StatusKey='active'", new { actingInstance });
            foreach (var suffix in new[] { "", "/runtime" })
            {
                var detail = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}{suffix}");
                var names = detail.GetProperty("attachmentNames"); Assert.AreEqual(1, names.EnumerateObject().Count());
                Assert.AreEqual("source-owned.png", names.GetProperty(capability.ToString("D")).GetString());
                Assert.IsFalse(names.TryGetProperty(originalFile.ToString("D"), out _));
            }
            Assert.AreEqual("source row", input.RootElement.GetProperty("rows")[0].GetProperty("label").GetString());
            await BytesAsync(source, sourceInstance, originalFile, FileBytes); await BytesAsync(target, actingInstance, capability, FileBytes);
            using var naked = await target.GetAsync(ContentPath(actingInstance, originalFile)); Assert.AreEqual(HttpStatusCode.Forbidden, naked.StatusCode);
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_read_capability WHERE CallId=@call AND DirectionKey='input'", new { call }),
                "准备循环和父子投影不能重复签发输入能力。");
            return new(originalFile, capability, actingInstance);
        }

        private static async Task<(object Patch, WorkerFileFacts Files)> PrepareFileReplyAsync(HttpClient target, Guid todoId, JsonElement todo, WorkerFileFacts facts)
        {
            var targetFile = await UploadViaWorkflowAsync(target,
                $"/api/v1/workflow/todos/{todoId:D}/attachment-uploads?expectedRevision={todo.GetProperty("revision").GetInt64()}&fieldKey=returnedFiles",
                "worker-target.png", WorkerTargetBytes);
            return (new { title = "worker result", returnedFiles = new[] { facts.InputCapability, targetFile }, secret = "target-private",
                rows = new[] { new { label = "worker target row", amount = 8 } } }, facts with { TargetFile = targetFile });
        }

        private static async Task VerifyWorkerFileResultAsync(DbConnection db, HttpClient source, HttpClient target, Guid sourceInstance,
            WorkerFileFacts facts, JsonElement values, string scenario)
        {
            await BytesAsync(source, sourceInstance, facts.SourceFile, FileBytes);
            Assert.AreEqual("source row", values.GetProperty("rows")[0].GetProperty("label").GetString());
            if (scenario == "worker_cancel")
            {
                Assert.IsFalse(values.TryGetProperty("returnedFiles", out _));
                using var denied = await target.GetAsync(ContentPath(facts.ActingInstance, facts.InputCapability));
                Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); return;
            }
            var returned = values.GetProperty("returnedFiles").EnumerateArray().Select(value => value.GetGuid()).ToArray();
            Assert.HasCount(2, returned); Assert.AreEqual(facts.SourceFile, returned[0]); Assert.AreNotEqual(facts.TargetFile!.Value, returned[1]);
            Assert.AreEqual("worker target row", values.GetProperty("returnedRows")[0].GetProperty("label").GetString());
            await BytesAsync(source, sourceInstance, returned[1], WorkerTargetBytes);
            await BytesAsync(target, facts.ActingInstance, facts.InputCapability, FileBytes);
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId IN @ids",
                new { ids = new[] { facts.InputCapability, returned[1] } }), "真实父子链不能为外租户能力建立伪造本地claim。");
        }

        private static async Task CleanupFileCapabilitiesAsync(FullNetApiFactory factory, DbConnection db, Guid call)
        {
            using var cleanup = await BuildAsync(factory); await cleanup.StartAsync();
            try
            {
                await UntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_read_capability WHERE CallId=@call AND StatusKey='active'", new { call }) == 0,
                    "撤销或取消后的文件能力必须由生产循环回收，不能删除原文件或手动释放能力。");
            }
            finally { await cleanup.StopAsync(); }
        }

        private static async Task VerifyWorkerFileRevocationAsync(FullNetApiFactory factory, DbConnection db, HttpClient source,
            HttpClient target, Guid instance, WorkerFileFacts facts, JsonElement values, Guid call, string scenario)
        {
            if (scenario == "worker_success")
            {
                using var deniedResult = await source.GetAsync(ContentPath(instance, values.GetProperty("returnedFiles")[1].GetGuid()));
                Assert.AreEqual(HttpStatusCode.Forbidden, deniedResult.StatusCode);
            }
            using var deniedInput = await target.GetAsync(ContentPath(facts.ActingInstance, facts.InputCapability));
            Assert.AreEqual(HttpStatusCode.Forbidden, deniedInput.StatusCode);
            await CleanupFileCapabilitiesAsync(factory, db, call);
            await BytesAsync(source, instance, facts.SourceFile, FileBytes);
        }
    }
}
