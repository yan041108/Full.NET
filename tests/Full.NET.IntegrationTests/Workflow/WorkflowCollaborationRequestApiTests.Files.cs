using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using DbUp;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    private static async Task RecoverFilesAsync(DatabaseProvider provider, string connection, DbConnection db)
    {
        var hashes = (await db.QueryAsync<string>("SELECT ContentHash FROM fn_files_read_capability ORDER BY ContentHash")).ToArray();
        var references = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_file_read_reference");
        Assert.IsGreaterThan(0, hashes.Length); Assert.IsGreaterThan(0, references);
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DROP INDEX ix_fn_files_read_capability_expiry ON fn_files_read_capability; ALTER TABLE fn_files_read_capability DROP CONSTRAINT ck_fn_files_read_capability_state; DROP INDEX ix_fn_workflow_file_read_reference_instance ON fn_workflow_file_read_reference; ALTER TABLE fn_workflow_file_read_reference DROP CONSTRAINT fk_fn_workflow_file_read_reference_attachment; ALTER TABLE fn_workflow_collaboration_call DROP CONSTRAINT ck_fn_workflow_collaboration_call_state; DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%338_WorkflowFileReadCapabilities.sql' OR ScriptName LIKE '%339_WorkflowCollaborationFiles.sql'"
            : "ALTER TABLE fn_files_read_capability DROP INDEX ix_fn_files_read_capability_expiry; ALTER TABLE fn_files_read_capability DROP CHECK ck_fn_files_read_capability_state; ALTER TABLE fn_workflow_file_read_reference DROP INDEX ix_fn_workflow_file_read_reference_instance; ALTER TABLE fn_workflow_file_read_reference DROP FOREIGN KEY fk_fn_workflow_file_read_reference_attachment; ALTER TABLE fn_workflow_collaboration_call DROP CHECK ck_fn_workflow_collaboration_call_state; DELETE FROM schemaversions WHERE ScriptName LIKE '%338_WorkflowFileReadCapabilities.sql' OR ScriptName LIKE '%339_WorkflowCollaborationFiles.sql'");
        foreach (var expected in new[] { 2, 0 })
        {
            var builder = provider == DatabaseProvider.SqlServer ? DeployChanges.To.SqlDatabase(connection) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains($".Migrations.{provider}.", StringComparison.Ordinal) && (name.EndsWith(".338_WorkflowFileReadCapabilities.sql", StringComparison.Ordinal) || name.EndsWith(".339_WorkflowCollaborationFiles.sql", StringComparison.Ordinal)))
                .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(expected, result.Scripts);
        }
        CollectionAssert.AreEqual(hashes, (await db.QueryAsync<string>("SELECT ContentHash FROM fn_files_read_capability ORDER BY ContentHash")).ToArray());
        Assert.AreEqual(references, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_file_read_reference"));
    }

    private static readonly byte[] FileBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII=");
    private static async Task<Guid> UploadViaWorkflowAsync(HttpClient client, string path, string name, byte[] bytes)
    {
        using var form = new MultipartFormDataContent(); using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new("image/png"); form.Add(file, "file", name);
        using var response = await client.PostAsync(path, form); var json = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json);
        return JsonSerializer.Deserialize<JsonElement>(json).GetProperty("fileId").GetGuid();
    }
    private static async Task DeniedUploadPermissionAsync(FullNetApiFactory factory)
    {
        var name = "file-read-only." + Guid.CreateVersion7().ToString("N");
        await factory.CreateHostIdentityAsync(name, [WorkflowPermissions.InstancesStart, WorkflowPermissions.TodosRead], password: FullNetApiFactory.TestPassword);
        using var client = factory.CreateClientForHost("localhost"); client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, name));
        foreach (var path in new[] { $"/api/v1/workflow/definition-versions/{Guid.CreateVersion7():D}/attachment-uploads?fieldKey=inputFiles",
                     $"/api/v1/workflow/todos/{Guid.CreateVersion7():D}/attachment-uploads?fieldKey=inputFiles&expectedRevision=1" })
        {
            using var form = new MultipartFormDataContent(); using var file = new ByteArrayContent(FileBytes); file.Headers.ContentType = new("image/png");
            form.Add(file, "file", "proof.png"); using var response = await client.PostAsync(path, form);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
    private static async Task PrepareFilesAsync(FullNetApiFactory factory)
    {
        await using var worker = factory.Services.CreateAsyncScope();
        worker.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        await worker.ServiceProvider.GetRequiredService<WorkflowCollaborationFilePreparationProcessor>().ProcessPendingAsync(default);
    }
    private static string ContentPath(Guid instance, Guid file) => $"/api/v1/workflow/instances/{instance:D}/form-attachments/{file:D}/content";
    private static async Task BytesAsync(HttpClient client, Guid instance, Guid file, byte[] bytes)
    {
        using var response = await client.GetAsync(ContentPath(instance, file));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        CollectionAssert.AreEqual(bytes, await response.Content.ReadAsByteArrayAsync());
    }

    private static async Task FileCaseAsync(FullNetApiFactory factory, DbConnection db, HttpClient source, HttpClient target,
        Guid? sourceTenant, Guid instance, WorkflowCollaborationCallRecord call,
        Guid grant, Guid originalFile, InsertFault fault, string scenario, int before, int beforeMessages, HttpClient? outsider, BrowserFileFlow? browser)
    {
        Assert.AreEqual("preparing", call.StatusKey);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id }));
        Assert.AreEqual(beforeMessages, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type", new { type = Full.NET.Modules.Workflow.Contracts.WorkflowCollaborationIntegrationEventTypes.Requested }));
        if (scenario == "files_early_cancel")
        {
            await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/cancel", new { expectedRevision = 1, reason = "文件准备前取消", idempotencyKey = "cancel-files-preparing" });
            await PrepareFilesAsync(factory);
            Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }));
            Assert.AreEqual(before + 1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance"));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_read_capability WHERE CallId=@id", new { id = call.Id }));
            return;
        }
        var hits = fault.FreezeHits; fault.Freeze = true;
        if (scenario == "files_paused")
        {
            await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/pause", new { expectedRevision = 1, reason = "准备期间暂停", idempotencyKey = "pause-files" });
            await PrepareFilesAsync(factory);
            Assert.AreEqual(hits, fault.FreezeHits);
            Assert.AreEqual("preparing", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }));
            Assert.AreEqual(32, await db.ExecuteScalarAsync<int>("SELECT Reserved FROM fn_workflow_exec_budget WHERE InstanceId=@instance", new { instance }));
            await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/resume", new { expectedRevision = 2, reason = "恢复原准备", idempotencyKey = "resume-files" });
        }
        await PrepareFilesAsync(factory); fault.Freeze = false;
        Assert.IsTrue(fault.FreezeHits > hits, "必须触及冻结写入故障，不能用准备早期拒绝冒充回滚");
        Assert.AreEqual("preparing", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_read_capability WHERE CallId=@id AND DirectionKey='input'", new { id = call.Id }));
        await Task.WhenAll(PrepareFilesAsync(factory), PrepareFilesAsync(factory));
        Assert.AreEqual("requested", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_read_capability WHERE CallId=@id AND DirectionKey='input'", new { id = call.Id }));
        var projection = await db.ExecuteScalarAsync<string>("SELECT ProjectionJson FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id });
        Assert.IsFalse(projection!.Contains(originalFile.ToString("D"), StringComparison.Ordinal)); Assert.IsFalse(projection.Contains("source-private", StringComparison.Ordinal));
        await RelayAsync(factory, db);
        var targetId = await db.ExecuteScalarAsync<Guid>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@id", new { id = call.Id });
        await VerifyActiveListsAsync(db, sourceTenant, target, outsider, targetId);
        var submission = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@targetId", new { targetId }))!);
        var inputCapability = submission.GetProperty("inputFiles")[0].GetGuid(); Assert.AreNotEqual(originalFile, inputCapability);
        Assert.AreEqual("source row", submission.GetProperty("rows")[0].GetProperty("label").GetString());
        await BytesAsync(target, targetId, inputCapability, FileBytes);
        using (var naked = await target.GetAsync(ContentPath(targetId, originalFile))) Assert.AreNotEqual(HttpStatusCode.OK, naked.StatusCode);
        await using (var foreignRecipient = factory.Services.CreateAsyncScope())
        {
            foreignRecipient.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new TenantContext(Guid.CreateVersion7(), "outsider", "第三方"));
            Assert.IsNull(await foreignRecipient.ServiceProvider.GetRequiredService<IWorkflowFileReadCapabilityStore>().DescribeAsync(inputCapability, call.Id));
        }
        var targetBytes = FileBytes.Concat(new byte[] { 23 }).ToArray();
        var todoId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@targetId AND StatusKey='active'", new { targetId });
        var todo = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}");
        if (browser?.RevokeVisible == true)
        {
            await browser.VerifyRevocationAsync(factory, todoId, inputCapability, originalFile, async () =>
                _ = await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke", new { expectedRevision = 2, idempotencyKey = "browser-visible-revoke", reason = "页面已打开时撤销" }));
            await browser.RenewRecipientClientAsync(target);
            await RelayAsync(factory, db);
            Assert.AreEqual("denied", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }));
            Assert.AreEqual("suspended", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
            Assert.AreEqual("cancelled", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@targetId", new { targetId }));
            using var deniedContent = await target.GetAsync(ContentPath(targetId, inputCapability)); Assert.AreEqual(HttpStatusCode.Forbidden, deniedContent.StatusCode);
            await BytesAsync(source, instance, originalFile, FileBytes);
            for (var pass = 0; pass < 4; pass++) await PrepareFilesAsync(factory);
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_read_capability WHERE CallId=@id AND StatusKey='active'", new { id = call.Id }));
            return;
        }
        Guid targetFile;
        if (browser is not null)
        {
            targetFile = await browser.ApproveAsync(factory, todoId, inputCapability, originalFile, targetBytes);
            await browser.RenewRecipientClientAsync(target);
        }
        else
        {
            targetFile = await UploadViaWorkflowAsync(target, $"/api/v1/workflow/todos/{todoId:D}/attachment-uploads?expectedRevision={todo.GetProperty("revision").GetInt64()}&fieldKey=returnedFiles", "target-owned.png", targetBytes);
            await PostAsync(target, $"/api/v1/workflow/todos/{todoId:D}/approve", new { expectedRevision = todo.GetProperty("revision").GetInt64(),
                expectedFormRevision = todo.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { returnedFiles = new[] { inputCapability, targetFile }, secret = "target-private", rows = new[] { new { label = "target result", amount = 8 } } },
                comment = "能力回传", idempotencyKey = "files-result" });
        }
        await RelayAsync(factory, db);
        Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@id", new { id = call.Id }));
        var result = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!);
        Assert.AreEqual("source-private", result.GetProperty("secret").GetString());
        Assert.AreEqual("source row", result.GetProperty("rows")[0].GetProperty("label").GetString());
        Assert.AreEqual("target result", result.GetProperty("returnedRows")[0].GetProperty("label").GetString());
        var returned = result.GetProperty("returnedFiles").EnumerateArray().Select(item => item.GetGuid()).ToArray();
        Assert.AreEqual(originalFile, returned[0]); Assert.AreNotEqual(targetFile, returned[1]);
        await BytesAsync(source, instance, originalFile, FileBytes); await BytesAsync(source, instance, returned[1], targetBytes);
        // 成功接纳只保留当前授权下的真实历史读取，不能再执行办理或上传。
        await BytesAsync(target, targetId, inputCapability, FileBytes);
        using (var history = await target.GetAsync($"/api/v1/workflow/todos/{todoId:D}")) Assert.AreEqual(HttpStatusCode.OK, history.StatusCode);
        await VerifyCompletedListsAsync(db, target, targetId, true);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId IN @ids", new { ids = new[] { inputCapability, returned[1] } }), "不能给外租户能力建立伪造的本地文件claim");
        await using (var worker = factory.Services.CreateAsyncScope())
        {
            var writer = worker.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            if (sourceTenant is { } id) writer.SetTenant(new TenantContext(id, "source", "源")); else writer.SetHost();
            await worker.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);
        }
        await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke", new { expectedRevision = 2, idempotencyKey = "revoke-files", reason = "结果后撤销文件共享" });
        using (var denied = await source.GetAsync(ContentPath(instance, returned[1]))) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        await VerifyCompletedListsAsync(db, target, targetId, false);
        await BytesAsync(source, instance, originalFile, FileBytes);
        for (var pass = 0; pass < 4; pass++) await PrepareFilesAsync(factory);
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_read_capability WHERE CallId=@id AND StatusKey='active'", new { id = call.Id }));
    }
}
