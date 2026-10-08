using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.RepairApproval;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    // 复用可靠审批的真实数据库与已发布定义，增加历史无日志及对账故障，不新增容器夹具。
    private static async Task VerifyControlledApprovalRepairAsync(FullNetApiFactory factory, HttpClient client, string token,
        EnterpriseRequestResponse original, DbConnection connection, IServiceProvider services, CancellationToken ct)
    {
        using var create = new HttpRequestMessage(HttpMethod.Post, BasePath) {
            Content = JsonContent.Create(new { requestNumber = $"FIX-{Guid.NewGuid():N}"[..16], title = "Legacy recovery probe",
                status = "Draft", totalAmount = 1m, applicantUserId = original.ApplicantUserId }) };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        create.Headers.Add("X-FullNet-Organization-Unit-Id", original.OrganizationUnitId.ToString("D"));
        using var created = await client.SendAsync(create, ct);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync(ct));
        var row = (await created.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(ct))!;
        var definition = await connection.ExecuteScalarAsync<Guid>("SELECT WorkflowDefinitionVersionId FROM demo_enterprise_request_approval_submission WHERE RequestId = @Id", new { original.Id });
        // 模拟旧版本的 Submitted 单据，没有 247 提交日志；流程本身仍通过真实所有者端口创建。
        Assert.AreEqual(1, await connection.ExecuteAsync("UPDATE demo_enterprise_request_enterprise_request SET Status = 'Submitted', Version = 2, UpdatedById = @Actor WHERE Id = @Id AND TenantId = @TenantId",
            new { row.Id, row.TenantId, Actor = original.UpdatedById }));
        var tenant = services.GetRequiredService<ICurrentTenantContextWriter>(); tenant.SetTenant(new(row.TenantId, "acme", "Acme"));
        var started = await services.GetRequiredService<IWorkflowInstanceStarter>().StartAsync(original.UpdatedById!.Value,
            new(definition, EnterpriseRequestWorkflowConstants.BusinessType, row.Id.ToString("D"), "{}", $"submit:{row.Id:D}:1", row.Title), ct);
        Assert.IsTrue(started.IsSuccess, started.Error?.Code);
        var instance = started.Value!.InstanceId; tenant.SetHost();
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.RecoveryRequired, (await ReadProgress(client, token, row.Id, ct)).DeliveryState);

        await Repair(HttpStatusCode.Unauthorized, null, instance, 2, "verify");
        await Repair(HttpStatusCode.Conflict, token, Guid.NewGuid(), 2, "verify");
        await Repair(HttpStatusCode.Conflict, token, instance, 3, "verify");
        await Repair(HttpStatusCode.BadRequest, token, instance, 2, " ");
        var intents = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE TenantId = @TenantId", new { row.TenantId });
        await Task.WhenAll(Repair(HttpStatusCode.NoContent, token, instance, 2, "已核对历史原启动回执"),
            Repair(HttpStatusCode.NoContent, token, instance, 2, "已核对历史原启动回执"));
        await Repair(HttpStatusCode.NoContent, token, instance, 2, "响应丢失后重试");
        var progress = await ReadProgress(client, token, row.Id, ct);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Started, progress.DeliveryState);
        Assert.AreEqual(instance, progress.WorkflowInstanceId); Assert.AreEqual(2L, progress.RequestVersion);
        Assert.AreEqual(1, await RepairCount());
        Assert.AreEqual(original.UpdatedById, await connection.ExecuteScalarAsync<Guid>("SELECT ActorUserId FROM demo_enterprise_request_approval_repair WHERE RequestId = @Id", new { row.Id }));
        Assert.AreEqual("已核对历史原启动回执", await connection.ExecuteScalarAsync<string>("SELECT Reason FROM demo_enterprise_request_approval_repair WHERE RequestId = @Id", new { row.Id }));
        Assert.AreEqual(intents, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE TenantId = @TenantId", new { row.TenantId }), "恢复不能重启或补发 Workflow 通知。");
        // 已有绑定丢失启动回执时，恢复记录不能占用同版本后续终态对账的唯一键。
        await connection.ExecuteAsync("UPDATE demo_enterprise_request_approval_submission SET StartedAtUtc = NULL WHERE RequestId = @Id", new { row.Id });
        await Repair(HttpStatusCode.NoContent, token, instance, 2, "恢复启动回执");
        Assert.AreEqual(2, await RepairCount());
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_approval_repair WHERE RequestId = @Id AND KindKey = 'start_receipt'", new { row.Id }));
        tenant.SetTenant(new(row.TenantId, "acme", "Acme"));
        Assert.IsTrue((await services.GetRequiredService<IWorkflowInstanceCanceller>().CancelAsync(original.UpdatedById.Value,
            new(instance, 1, "verify reconcile", "repair-cancel"), ct)).IsSuccess);
        // 恢复记录写入失败时，真实绑定、终态和版本整组回滚。
        var repair = ActivatorUtilities.CreateInstance<EnterpriseRequestApprovalRepairService>(services,
            new FailRepairRecordExecutor(services.GetRequiredService<ICommandExecutor>()));
        Assert.IsFalse((await repair.RepairAsync(row.Id, new(instance, 2, "故障注入"), original.UpdatedById.Value, true, ct)).IsSuccess);
        Assert.AreEqual("Submitted", await connection.ExecuteScalarAsync<string>("SELECT Status FROM demo_enterprise_request_enterprise_request WHERE Id = @Id", new { row.Id }));
        Assert.AreEqual(2L, await connection.ExecuteScalarAsync<long>("SELECT Version FROM demo_enterprise_request_enterprise_request WHERE Id = @Id", new { row.Id }));
        Assert.IsNull(await connection.ExecuteScalarAsync<string>("SELECT FinalStatus FROM demo_enterprise_request_approval_submission WHERE RequestId = @Id", new { row.Id }));
        Assert.AreEqual(2, await RepairCount()); tenant.SetHost();
        await Repair(HttpStatusCode.NoContent, token, instance, 2, "核对取消终态");
        await Repair(HttpStatusCode.NoContent, token, instance, 2, "再次重放");
        progress = await ReadProgress(client, token, row.Id, ct);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Finalized, progress.DeliveryState);
        Assert.AreEqual("Cancelled", progress.RequestStatus); Assert.AreEqual(3L, progress.RequestVersion); Assert.AreEqual(3, await RepairCount());
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE Id = @Id", new { Id = instance }));
        await VerifyApprovalRepairMigrationReentryAsync(factory, connection, row.Id);

        async Task Repair(HttpStatusCode expected, string? accessToken, Guid flow, long version, string reason)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{row.Id:D}/repair-approval") {
                Content = JsonContent.Create(new RepairEnterpriseRequestApprovalRequest(flow, version, reason)) };
            if (accessToken is not null) request.Headers.Authorization = new("Bearer", accessToken);
            using var response = await client.SendAsync(request, ct);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        Task<int> RepairCount() => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_approval_repair WHERE TenantId = @TenantId AND RequestId = @Id", new { row.TenantId, row.Id });
    }

    private sealed class FailRepairRecordExecutor(ICommandExecutor inner) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken ct = default) =>
            statement.Name == "enterprise_request.approval.insert_repair" ? Task.FromResult(0) : inner.ExecuteAsync(statement, parameters, ct);
    }
}
