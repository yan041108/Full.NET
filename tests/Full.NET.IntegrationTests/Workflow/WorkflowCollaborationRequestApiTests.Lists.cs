using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    private static async Task<string> DefinitionKeyAsync(DbConnection db, Guid instance) => await db.ExecuteScalarAsync<string>("""
        SELECT definition.DefinitionKey FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_definition_version AS version ON version.Id=instance.DefinitionVersionId
        INNER JOIN fn_workflow_definition AS definition ON definition.Id=version.DefinitionId WHERE instance.Id=@instance
        """, new { instance }).ConfigureAwait(false) ?? throw new InvalidDataException("Workflow definition key missing.");

    private static async Task ListCountAsync(HttpClient client, string path, long expected, Guid? included = null, string totalProperty = "total")
    {
        using var response = await client.GetAsync(path); var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        using var json = System.Text.Json.JsonDocument.Parse(text); var result = json.RootElement;
        Assert.AreEqual(expected, result.GetProperty(totalProperty).GetInt64(), path + ": " + text);
        var items = result.GetProperty("items");
        Assert.AreEqual(Math.Min(expected, 1), (long)items.GetArrayLength(), "分页大小1与授权后的总数必须一致");
        if (included is { } id) Assert.AreEqual(id, items[0].GetProperty("id").GetGuid());
    }

    private static async Task VerifyActiveListsAsync(DbConnection db, Guid? sourceTenant, HttpClient target, HttpClient? outsider, Guid targetId)
    {
        var key = Uri.EscapeDataString(await DefinitionKeyAsync(db, targetId));
        var all = $"/api/v1/workflow/instances?definitionKey={key}&pageSize=1";
        var mine = $"/api/v1/workflow/instances/mine?definitionKey={key}&pageSize=1";
        var pending = $"/api/v1/workflow/todos/mine?definitionKey={key}&pageSize=1";
        await ListCountAsync(target, all, 1, targetId); await ListCountAsync(target, mine, 1, targetId); await ListCountAsync(target, pending, 1);
        if (outsider is not null)
        {
            // 同租户超级管理员也不是政策固定接收成员，不能靠全局实例权限绕过。
            await ListCountAsync(outsider, all, 0);
            using var denied = await outsider.GetAsync($"/api/v1/workflow/instances/{targetId:D}"); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        if (sourceTenant is { } tenant)
        {
            // 不发送失效通知或依赖版本变更，直接证明目标查询每次从Port读取源活动状态。
            await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET IsActive=0 WHERE Id=@tenant", new { tenant });
            try
            {
                await ListCountAsync(target, all, 0); await ListCountAsync(target, pending, 0);
                using var denied = await target.GetAsync($"/api/v1/workflow/instances/{targetId:D}"); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            finally { await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET IsActive=1 WHERE Id=@tenant", new { tenant }); }
            await ListCountAsync(target, all, 1, targetId);
        }
        var task = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        // 只准备Workflow自己的真实任务行；验证列表，不伪造恢复成功或执行外部副作用。
        await db.ExecuteAsync("""
            INSERT INTO fn_workflow_recovery_task
                (Id,TenantId,ScopeKey,TenantScopeKey,InstanceId,StepId,KindKey,StatusKey,AttemptCount,Revision,LeaseOwnerKey,LeaseExpiresAtUtc,
                 LeaseGeneration,NextAttemptAtUtc,LastError,CreatedAtUtc,UpdatedAtUtc)
            SELECT @task,TenantId,ScopeKey,TenantScopeKey,Id,NULL,'stuck_instance','failed',0,1,NULL,NULL,0,NULL,'list fixture',@now,@now
            FROM fn_workflow_instance WHERE Id=@targetId
            """, new { task, targetId, now });
        using (var response = await target.GetAsync("/api/v1/workflow/recovery-tasks?pageSize=1"))
        {
            var body = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
            using var page = System.Text.Json.JsonDocument.Parse(body); var root = page.RootElement;
            Assert.IsTrue(root.GetProperty("total").GetInt64() >= 1, body);
            Assert.AreEqual(1, root.GetProperty("items").GetArrayLength());
            Assert.AreEqual(task, root.GetProperty("items")[0].GetProperty("id").GetGuid());
        }
        await db.ExecuteAsync("UPDATE fn_workflow_recovery_task SET StatusKey='succeeded' WHERE Id=@task", new { task });
    }

    private static async Task VerifyCompletedListsAsync(DbConnection db, HttpClient target, Guid targetId, bool allowed)
    {
        var key = Uri.EscapeDataString(await DefinitionKeyAsync(db, targetId));
        await ListCountAsync(target, $"/api/v1/workflow/instances?definitionKey={key}&pageSize=1", allowed ? 1 : 0, allowed ? targetId : null);
        await ListCountAsync(target, $"/api/v1/workflow/todos/mine/history?definitionKey={key}&pageSize=1", allowed ? 1 : 0);
        var ordinary = await db.ExecuteScalarAsync<long>("""
            SELECT COUNT(*) FROM fn_workflow_recovery_task AS task
            INNER JOIN fn_workflow_instance AS instance ON instance.Id=task.InstanceId
            WHERE task.TenantScopeKey=(SELECT TenantScopeKey FROM fn_workflow_instance WHERE Id=@targetId)
                AND NOT EXISTS (SELECT 1 FROM fn_workflow_collaboration_origin AS origin WHERE origin.Id=instance.Id)
            """, new { targetId });
        // 已收口的其他授权目标任务仍可能可读；本例只在当前目标授权被撤销后验证其任务退出结果集。
        using var response = await target.GetAsync("/api/v1/workflow/recovery-tasks?pageSize=100"); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var tasks = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.AreEqual(allowed, tasks.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("instanceId").GetGuid() == targetId));
        Assert.IsTrue(tasks.GetProperty("total").GetInt64() >= ordinary);
    }

    private static async Task VerifyClaimListAsync(FullNetApiFactory factory, DbConnection db, HttpClient source, HttpClient target, Guid instance, Guid call, Guid grant)
    {
        await RelayAsync(factory, db);
        var targetId = await db.ExecuteScalarAsync<Guid>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@call", new { call });
        var pool = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_claim WHERE InstanceId=@targetId AND StatusKey='available'", new { targetId });
        await ListCountAsync(target, "/api/v1/workflow/claim-pools?pageSize=1", 1, pool, "totalCount");
        await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke", new { expectedRevision = 2, idempotencyKey = "revoke-claim-list", reason = "实时退出候选池列表" });
        await ListCountAsync(target, "/api/v1/workflow/claim-pools?pageSize=1", 0, totalProperty: "totalCount");
        await RelayAsync(factory, db);
        Assert.AreEqual("denied", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }));
        Assert.AreEqual("suspended", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
    }
}
