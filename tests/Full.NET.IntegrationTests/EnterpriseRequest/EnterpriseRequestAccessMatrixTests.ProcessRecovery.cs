using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

public sealed partial class EnterpriseRequestAccessMatrixTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Worker_process_crashes_recover_submission_and_business_writeback_without_duplicate_effects(DatabaseProvider provider)
    {
        var root = Environment.GetEnvironmentVariable("FULLNET_ENTERPRISE_PROCESS_EVIDENCE")
            ?? Directory.CreateTempSubdirectory("fullnet-enterprise-process-").FullName;
        var evidence = Path.Combine(root, provider + "-" + Guid.CreateVersion7().ToString("N")); Directory.CreateDirectory(evidence);
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection,
            new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = Path.Combine(evidence, "keys") }, includeEnterpriseRequest: true);
        await factory.InitializeAsync(); using var owner = factory.CreateClientForHost("localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenant = (await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(t => t.Identifier == "acme");
        var token = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, hostToken, tenant.Id);
        owner.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(owner, token);
        var unit = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(owner, token, default);
        var actor = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(owner, token, default);
        var created = await SendAsync(owner, HttpMethod.Post, Root + "/aggregate", Create(actor, "真实进程中断恢复"), HttpStatusCode.OK, unit);
        var requestId = created.GetProperty("master").GetProperty("id").GetGuid(); var path = $"{Root}/{requestId:D}";
        await SendAsync(owner, HttpMethod.Post, path + "/submit-for-approval", null, HttpStatusCode.OK);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var submissionGate = Path.Combine(evidence, "submission.gate"); var outcomeGate = Path.Combine(evidence, "outcome.gate");
        var ids = new List<int>();
        await using (var first = await EnterpriseRequestWorkerProcess.StartAsync(factory, evidence, "submission", submissionGate, 1))
        {
            ids.Add(first.Id); await UntilAsync(() => Task.FromResult(File.Exists(submissionGate)));
            var messageId = Guid.Parse(await File.ReadAllTextAsync(submissionGate));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id=@messageId AND ProcessedAtUtc IS NOT NULL", new { messageId }));
            await first.CrashAsync();
        }
        var instanceId = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_workflow_instance WHERE BusinessId=@id", new { id = requestId.ToString("D") });
        var todoId = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instanceId AND StatusKey='active'", new { instanceId });
        var submittedMessageId = Guid.Parse(await File.ReadAllTextAsync(submissionGate));
        string? committedVersion;
        await using (var second = await EnterpriseRequestWorkerProcess.StartAsync(factory, evidence, "outcome", outcomeGate, 2))
        {
            ids.Add(second.Id);
            // 等待生产租约自然过期并重投，不写 SQL 缩短锁期或直接调用恢复处理器。
            await UntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id=@submittedMessageId AND ProcessedAtUtc IS NOT NULL", new { submittedMessageId }) == 1);
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE BusinessId=@id", new { id = requestId.ToString("D") }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo WHERE InstanceId=@instanceId", new { instanceId }));
            var detail = await SendAsync(owner, HttpMethod.Get, $"/api/v1/workflow/todos/{todoId:D}/runtime", null, HttpStatusCode.OK);
            await SendAsync(owner, HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", new { expectedRevision = 1,
                expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, comment = "崩溃恢复后批准", idempotencyKey = "process-" + requestId.ToString("N") }, HttpStatusCode.OK);
            await UntilAsync(() => Task.FromResult(File.Exists(outcomeGate)));
            var committed = await SendAsync(owner, HttpMethod.Get, path, null, HttpStatusCode.OK);
            Assert.AreEqual("Approved", committed.GetProperty("status").GetString()); committedVersion = committed.GetProperty("version").GetString();
            var messageId = Guid.Parse(await File.ReadAllTextAsync(outcomeGate));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id=@messageId AND ProcessedAtUtc IS NOT NULL", new { messageId }));
            await second.CrashAsync();
        }
        var terminalMessageId = Guid.Parse(await File.ReadAllTextAsync(outcomeGate));
        await using (var third = await EnterpriseRequestWorkerProcess.StartAsync(factory, evidence, "none", Path.Combine(evidence, "unused.gate"), 3))
        {
            ids.Add(third.Id);
            await UntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id=@terminalMessageId AND ProcessedAtUtc IS NOT NULL", new { terminalMessageId }) == 1);
            var final = await SendAsync(owner, HttpMethod.Get, path, null, HttpStatusCode.OK);
            Assert.AreEqual("Approved", final.GetProperty("status").GetString()); Assert.AreEqual(committedVersion, final.GetProperty("version").GetString());
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE BusinessId=@id", new { id = requestId.ToString("D") }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@instanceId AND OperationKey='todo.approve'", new { instanceId }));
        }
        Assert.AreEqual(3, ids.Distinct().Count());
        await File.WriteAllTextAsync(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new { provider = provider.ToString(), requestId, instanceId,
            processIds = ids, submittedMessageId, terminalMessageId, committedVersion, passed = true }));
        Console.WriteLine("真实进程恢复证据：" + evidence);

        static async Task UntilAsync(Func<Task<bool>> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (!await condition()) await Task.Delay(100, timeout.Token);
        }
    }
}
