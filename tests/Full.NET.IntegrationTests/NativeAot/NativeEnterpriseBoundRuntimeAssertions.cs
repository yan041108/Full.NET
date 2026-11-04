using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>复用企业原生场景的身份与宿主，验证绑定回执在暂停、恢复及故障取消期间的稳定性。</summary>
internal static partial class NativeEnterpriseBusinessAssertions
{
    private static async Task<JsonElement> WaitBoundStartAsync(BusinessHttp http, Guid requestId, JsonElement queued)
    {
        var started = await WaitAsync(http, $"{Requests}/{requestId:D}/approval-progress",
            value => value.GetProperty("deliveryState").GetString() == "started");
        AssertBoundPending(started, started);
        Assert.AreEqual(queued.GetProperty("workflowInstanceId").GetGuid(), started.GetProperty("workflowInstanceId").GetGuid());
        Assert.AreEqual(queued.GetProperty("submittedVersion").GetString(), started.GetProperty("submittedVersion").GetString());
        return started;
    }

    private static async Task<JsonElement> VerifyPausedBoundRuntimeAsync(BusinessHttp http, Guid requestId, JsonElement queued)
    {
        var before = await WaitBoundStartAsync(http, requestId, queued);
        var instanceId = before.GetProperty("workflowInstanceId").GetGuid();
        var path = $"/api/v1/workflow/instances/{instanceId:D}";
        var running = await http.SendAsync(HttpMethod.Get, path);
        Assert.AreEqual("active", running.GetProperty("statusKey").GetString());
        Assert.AreEqual(requestId, running.GetProperty("businessId").GetGuid());
        var todoId = running.GetProperty("activeTodoId").GetGuid();
        var todo = await http.SendAsync(HttpMethod.Get, $"/api/v1/workflow/todos/{todoId:D}");
        var revision = ReadBoundInteger(running.GetProperty("revision"));
        await SendBoundLifecycleAsync(http, path, "pause", revision, Guid.NewGuid());
        var paused = await http.SendAsync(HttpMethod.Get, path);
        Assert.AreEqual("suspended", paused.GetProperty("statusKey").GetString());
        AssertBoundPending(await http.SendAsync(HttpMethod.Get, $"{Requests}/{requestId:D}/approval-progress"), before);
        var approval = new { expectedRevision = ReadBoundInteger(todo.GetProperty("revision")), fieldPatch = new { }, comment = "原生恢复后审批", idempotencyKey = Guid.NewGuid().ToString("D") };
        await http.SendAsync(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", approval, HttpStatusCode.Conflict);
        await SendBoundLifecycleAsync(http, path, "recover", revision, Guid.NewGuid(), HttpStatusCode.Conflict);
        var recoverKey = Guid.NewGuid();
        var pausedRevision = ReadBoundInteger(paused.GetProperty("revision"));
        await SendBoundLifecycleAsync(http, path, "recover", pausedRevision, recoverKey);
        await SendBoundLifecycleAsync(http, path, "recover", pausedRevision, recoverKey);
        var recovered = await http.SendAsync(HttpMethod.Get, path);
        Assert.AreEqual("active", recovered.GetProperty("statusKey").GetString());
        Assert.AreEqual(pausedRevision + 1, ReadBoundInteger(recovered.GetProperty("revision")));
        Assert.AreEqual(todoId, recovered.GetProperty("activeTodoId").GetGuid());
        AssertBoundPending(await http.SendAsync(HttpMethod.Get, $"{Requests}/{requestId:D}/approval-progress"), before);
        // 暂停期间被拒绝的动作不能占用成功幂等键；恢复后使用新的键并验证重放。
        var approveKey = Guid.NewGuid();
        var approved = new { expectedRevision = ReadBoundInteger(todo.GetProperty("revision")), fieldPatch = new { }, comment = "原生恢复后审批", idempotencyKey = approveKey.ToString("D") };
        await http.SendAsync(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", approved);
        await http.SendAsync(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", approved);
        return await WaitBoundFinalAsync(http, requestId, before, "Approved");
    }

    /// <summary>只在调用方创建的独占数据库中构造绑定残留；一项待办和一项实例均必须精确匹配。</summary>
    private static async Task InjectNativeBoundFailureAsync(DatabaseProvider provider, string connectionString,
        Guid tenantId, Guid requestId, Guid instanceId)
    {
        await using var connection = CreateBoundConnection(provider, connectionString);
        await connection.OpenAsync();
        if (provider == DatabaseProvider.SqlServer)
            await connection.ExecuteAsync("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;");
        await using var transaction = await connection.BeginTransactionAsync();
        var parameters = new { TenantId = tenantId, RequestId = requestId, InstanceId = instanceId, Now = DateTime.UtcNow, Expiry = DateTime.UtcNow.AddMinutes(-1) };
        Assert.AreEqual(1, await connection.ExecuteAsync("""
            UPDATE fn_workflow_todo SET StatusKey='completed', CompletedAtUtc=@Now, ResultActionKey='system'
            WHERE InstanceId=@InstanceId AND StatusKey='active'
              AND EXISTS (SELECT 1 FROM fn_workflow_instance WHERE Id=@InstanceId AND TenantId=@TenantId)
              AND EXISTS (SELECT 1 FROM demo_enterprise_request_approval_submission
                WHERE WorkflowInstanceId=@InstanceId AND TenantId=@TenantId AND RequestId=@RequestId)
            """, parameters, transaction));
        Assert.AreEqual(1, await connection.ExecuteAsync("""
            UPDATE fn_workflow_instance SET LeaseOwnerKey='native-owned-fault', LeaseExpiresAtUtc=@Expiry
            WHERE Id=@InstanceId AND TenantId=@TenantId AND StatusKey='active'
              AND EXISTS (SELECT 1 FROM demo_enterprise_request_approval_submission
                WHERE WorkflowInstanceId=@InstanceId AND TenantId=@TenantId AND RequestId=@RequestId)
            """, parameters, transaction));
        await transaction.CommitAsync();
    }

    private static async Task<JsonElement> VerifyFailedBoundCancellationAsync(BusinessHttp http, DatabaseProvider provider,
        string connectionString, Guid tenantId, Guid requestId, JsonElement before)
    {
        var instanceId = before.GetProperty("workflowInstanceId").GetGuid();
        var path = $"/api/v1/workflow/instances/{instanceId:D}";
        var exhausted = await WaitAsync(http, path, value => value.GetProperty("statusKey").GetString() == "suspended");
        Assert.AreEqual(JsonValueKind.Null, exhausted.GetProperty("activeTodoId").ValueKind);
        AssertBoundPending(await http.SendAsync(HttpMethod.Get, $"{Requests}/{requestId:D}/approval-progress"), before);
        await using var connection = CreateBoundConnection(provider, connectionString);
        await connection.OpenAsync();
        // 实例暂停与恢复任务记账不是同一观察点；预算内等待实际死信，不能凭暂停推导任务已持久化。
        var deadLetterWait = Stopwatch.StartNew();
        long deadLetters;
        do
        {
            deadLetters = await connection.ExecuteScalarAsync<long>("""
                SELECT COUNT(*) FROM fn_workflow_recovery_task
                WHERE InstanceId=@InstanceId AND TenantId=@TenantId AND StatusKey='dead_lettered' AND AttemptCount=1
                """, new { InstanceId = instanceId, TenantId = tenantId });
            Assert.IsTrue(deadLetters is 0 or 1, "本轮只能耗尽一份精确绑定的恢复任务。");
            if (deadLetters == 1) break;
            await Task.Delay(250);
        } while (deadLetterWait.Elapsed < TimeSpan.FromSeconds(90));
        Assert.AreEqual(1L, deadLetters, "真实原生 Worker 必须持久化一次耗尽死信。");
        var revision = ReadBoundInteger(exhausted.GetProperty("revision"));
        await SendBoundLifecycleAsync(http, path, "recover", revision, Guid.NewGuid(), HttpStatusCode.Conflict);
        await SendBoundLifecycleAsync(http, path, "cancel", revision - 1, Guid.NewGuid(), HttpStatusCode.Conflict);
        AssertBoundPending(await http.SendAsync(HttpMethod.Get, $"{Requests}/{requestId:D}/approval-progress"), before);
        var cancelKey = Guid.NewGuid();
        await SendBoundLifecycleAsync(http, path, "cancel", revision, cancelKey);
        await SendBoundLifecycleAsync(http, path, "cancel", revision, cancelKey);
        var final = await WaitBoundFinalAsync(http, requestId, before, "Cancelled");
        var cancelled = await http.SendAsync(HttpMethod.Get, path);
        Assert.AreEqual("cancelled", cancelled.GetProperty("statusKey").GetString());
        Assert.AreEqual(revision + 1, ReadBoundInteger(cancelled.GetProperty("revision")));
        return final;
    }

    private static Task<JsonElement> SendBoundLifecycleAsync(BusinessHttp http, string path, string action,
        long revision, Guid key, HttpStatusCode expected = HttpStatusCode.OK) => http.SendAsync(HttpMethod.Post, path + "/" + action,
            new { expectedRevision = revision, reason = "原生绑定运行验收", idempotencyKey = key.ToString("D") }, expected);

    private static async Task<JsonElement> WaitBoundFinalAsync(BusinessHttp http, Guid requestId, JsonElement before, string status)
    {
        var final = await WaitAsync(http, $"{Requests}/{requestId:D}/approval-progress", value =>
            value.GetProperty("deliveryState").GetString() == "finalized"
            && value.GetProperty("finalNotification").ValueKind == JsonValueKind.Object
            && ReadBoundInteger(value.GetProperty("finalNotification").GetProperty("pendingDeliveryCount")) == 0);
        Assert.AreEqual(status, final.GetProperty("requestStatus").GetString());
        Assert.AreEqual(ReadBoundInteger(before.GetProperty("requestVersion")) + 1, ReadBoundInteger(final.GetProperty("requestVersion")));
        AssertBoundIdentity(final, before);
        Assert.AreNotEqual(JsonValueKind.Null, final.GetProperty("completedAtUtc").ValueKind);
        var notification = final.GetProperty("finalNotification");
        Assert.AreNotEqual(Guid.Empty, notification.GetProperty("intentId").GetGuid());
        Assert.AreEqual(0L, ReadBoundInteger(notification.GetProperty("failedDeliveryCount")));
        Assert.AreEqual(0L, ReadBoundInteger(notification.GetProperty("deadLetteredDeliveryCount")));
        var business = await http.SendAsync(HttpMethod.Get, $"{Requests}/{requestId:D}");
        Assert.AreEqual(status, business.GetProperty("status").GetString());
        Assert.AreEqual(final.GetProperty("requestVersion").GetString(), business.GetProperty("version").GetString());
        return final;
    }

    /// <summary>先从消息所有者读取权威积压，再用新租户令牌核对业务版本、通知意图和唯一站内信。</summary>
    private static async Task<BusinessHttp> VerifyBoundRuntimeDrainAsync(BusinessHttp tenant, Guid tenantId,
        IReadOnlyList<(Guid RequestId, JsonElement Final)> completed)
    {
        var host = await tenant.ChangeContextAsync(null);
        await WaitAsync(host, "/api/v1/messaging/delivery-status/", value =>
            new[] { "pendingCount", "dueRetryCount", "activeLeaseCount", "deadLetterCount" }
                .All(key => ReadBoundInteger(value.GetProperty("backlog").GetProperty(key)) == 0));
        tenant = await host.ChangeContextAsync(tenantId);
        var inbox = await tenant.SendAsync(HttpMethod.Get, "/api/v1/notifications/my-inbox-messages?page=1&pageSize=100");
        foreach (var (requestId, final) in completed)
        {
            var current = await tenant.SendAsync(HttpMethod.Get, $"{Requests}/{requestId:D}/approval-progress");
            Assert.AreEqual(final.GetProperty("requestVersion").GetString(), current.GetProperty("requestVersion").GetString());
            Assert.AreEqual(final.GetProperty("finalNotification").GetProperty("intentId").GetGuid(), current.GetProperty("finalNotification").GetProperty("intentId").GetGuid());
            var title = final.GetProperty("requestStatus").GetString() == "Approved" ? "审批已完成" : "审批已取消";
            Assert.AreEqual(1, inbox.GetProperty("items").EnumerateArray().Count(message =>
                message.GetProperty("title").GetString() == title && (message.GetProperty("content").GetString()?.Contains(requestId.ToString("D"), StringComparison.Ordinal) ?? false)));
        }
        return tenant;
    }

    private static void AssertBoundPending(JsonElement current, JsonElement before)
    {
        Assert.AreEqual("Submitted", current.GetProperty("requestStatus").GetString());
        Assert.AreEqual("started", current.GetProperty("deliveryState").GetString());
        AssertBoundIdentity(current, before);
        Assert.AreEqual(before.GetProperty("requestVersion").GetString(), current.GetProperty("requestVersion").GetString());
        Assert.AreEqual(JsonValueKind.Null, current.GetProperty("completedAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, current.GetProperty("finalNotification").ValueKind);
    }

    private static void AssertBoundIdentity(JsonElement current, JsonElement before)
    {
        foreach (var key in new[] { "workflowInstanceId", "submittedVersion", "submittedAtUtc", "startedAtUtc" })
            Assert.AreEqual(before.GetProperty(key).GetRawText(), current.GetProperty(key).GetRawText(), key);
    }

    private static long ReadBoundInteger(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? long.Parse(value.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture) : value.GetInt64();

    private static DbConnection CreateBoundConnection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(connectionString)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
