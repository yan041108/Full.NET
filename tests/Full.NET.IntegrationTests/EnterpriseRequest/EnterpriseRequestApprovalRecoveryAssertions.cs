using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.EnterpriseRequest;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    // 使用真实业务 SQL 和 Workflow 端口；故障只注入回执写入，流程事务已实际提交。
    private static async Task VerifyReliableApprovalRecoveryAsync(FullNetApiFactory factory, HttpClient client,
        string token, EnterpriseRequestResponse submitted, CancellationToken ct)
    {
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        var submission = await connection.QuerySingleAsync<ApprovalRecoveryProbe>("""
            SELECT Id, TenantId, RequestId, WorkflowInstanceId, SubmittedById, RequestVersion
            FROM demo_enterprise_request_approval_submission WHERE TenantId = @TenantId AND RequestId = @Id
            """, new { submitted.TenantId, submitted.Id });
        Assert.AreEqual(submitted.Version, submission.RequestVersion);
        var message = await connection.QuerySingleAsync<ApprovalOutboxProbe>("""
            SELECT Id, TenantId, Payload, OccurredAtUtc FROM fn_outbox_message
            WHERE TenantId = @TenantId AND MessageType = @MessageType
            """, new { submitted.TenantId, MessageType = EnterpriseRequestApprovalSubmittedIntegrationEvent.EventType });
        using (var replay = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{submitted.Id:D}/submit-for-approval"))
        {
            replay.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(replay, ct);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_approval_submission WHERE RequestId = @Id", new { submitted.Id }));
        var assembly = typeof(EnterpriseRequestModule).Assembly;
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ICurrentTenantContextWriter>(); tenant.SetHost();
        var commands = services.GetRequiredService<ICommandExecutor>();
        var handler = (IIntegrationEventHandler)ActivatorUtilities.CreateInstance(services,
            assembly.GetType("Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval.EnterpriseRequestApprovalSubmittedHandler", true)!,
            new LoseStartReceiptOnceExecutor(commands));
        var context = new IntegrationEventContext(message.Id, handler.EventType, 1, message.TenantId, null, message.OccurredAtUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(context, message.Payload, ct));
        Assert.IsTrue(tenant.IsHost, "消费失败后必须恢复 Worker Host 上下文。");
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE Id = @Id", new { Id = submission.WorkflowInstanceId }));
        Assert.IsNull(await connection.ExecuteScalarAsync<DateTime?>("SELECT StartedAtUtc FROM demo_enterprise_request_approval_submission WHERE Id = @Id", new { submission.Id }));

        tenant.SetTenant(new TenantContext(submitted.TenantId, "acme", "Acme"));
        var cancelled = await services.GetRequiredService<IWorkflowInstanceCanceller>().CancelAsync(submission.SubmittedById,
            new CancelWorkflowInstanceCommand(submission.WorkflowInstanceId, 1, "故障恢复验收", "recovery-cancel"), ct);
        Assert.IsTrue(cancelled.IsSuccess, cancelled.Error?.Code);
        tenant.SetHost();
        var outcomeType = assembly.GetType("Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes.EnterpriseRequestWorkflowOutcomeService", true)!;
        var outcome = ActivatorUtilities.CreateInstance(services, outcomeType, new LoseFinalReceiptOnceExecutor(commands));
        var sink = (IWorkflowInstanceCancelledSink)ActivatorUtilities.CreateInstance(services,
            assembly.GetType("Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes.WorkflowInstanceCancelledEnterpriseRequestSink", true)!, outcome);
        var terminal = new WorkflowInstanceCancelledIntegrationEvent(submission.WorkflowInstanceId, submission.SubmittedById,
            EnterpriseRequestWorkflowConstants.BusinessType, submitted.Id.ToString("D"), DateTimeOffset.UtcNow);
        var terminalContext = new IntegrationEventContext(Guid.CreateVersion7(), WorkflowNotificationIntegrationEventTypes.InstanceCancelled,
            1, submitted.TenantId, null, DateTimeOffset.UtcNow);
        // 外来实例的同业务标识不能回写；合法终态允许在启动回执之前到达。
        await sink.HandleAsync(terminalContext, terminal with { InstanceId = Guid.CreateVersion7() }, ct);
        Assert.AreEqual("Submitted", await Status());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.HandleAsync(terminalContext, terminal, ct));
        Assert.AreEqual("Submitted", await Status(), "终态回执失败必须回滚申请状态。");
        Assert.AreEqual(submitted.Version, await connection.ExecuteScalarAsync<long>("SELECT Version FROM demo_enterprise_request_enterprise_request WHERE Id = @Id", new { submitted.Id }));
        Assert.IsNull(await connection.ExecuteScalarAsync<string>("SELECT FinalStatus FROM demo_enterprise_request_approval_submission WHERE Id = @Id", new { submission.Id }));
        await sink.HandleAsync(terminalContext, terminal, ct);
        await sink.HandleAsync(terminalContext, terminal, ct);
        Assert.AreEqual("Cancelled", await Status());
        await handler.HandleAsync(context, message.Payload, ct);
        await handler.HandleAsync(context, message.Payload, ct);
        Assert.IsTrue(tenant.IsHost);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE TenantId = @TenantId AND BusinessType = @BusinessType AND BusinessId = @BusinessId",
            new { submitted.TenantId, EnterpriseRequestWorkflowConstants.BusinessType, BusinessId = submitted.Id.ToString("D") }));
        Assert.IsNotNull(await connection.ExecuteScalarAsync<DateTime?>("SELECT StartedAtUtc FROM demo_enterprise_request_approval_submission WHERE Id = @Id", new { submission.Id }));
        Assert.AreEqual(submitted.Version + 1, await connection.ExecuteScalarAsync<long>("SELECT Version FROM demo_enterprise_request_enterprise_request WHERE Id = @Id", new { submitted.Id }));

        Task<string?> Status() => connection.ExecuteScalarAsync<string>("SELECT Status FROM demo_enterprise_request_enterprise_request WHERE Id = @Id", new { submitted.Id });
    }

    private sealed record ApprovalRecoveryProbe(Guid Id, Guid TenantId, Guid RequestId, Guid WorkflowInstanceId, Guid SubmittedById, long RequestVersion);
    private sealed record ApprovalOutboxProbe(Guid Id, Guid TenantId, byte[] Payload, DateTimeOffset OccurredAtUtc);
    private sealed class LoseStartReceiptOnceExecutor(ICommandExecutor inner) : ICommandExecutor
    {
        private bool lost;
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (!lost && statement.Name == "enterprise_request.approval.mark_started")
            {
                lost = true;
                throw new InvalidOperationException("test.lost_start_receipt");
            }
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }

    private static async Task VerifySubmissionRollbackAsync(FullNetApiFactory factory, EnterpriseRequestResponse draft, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetTenant(new TenantContext(draft.TenantId, "acme", "Acme"));
        var type = typeof(EnterpriseRequestModule).Assembly.GetType(
            "Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval.SubmitEnterpriseRequestForApprovalService", true)!;
        var service = ActivatorUtilities.CreateInstance(scope.ServiceProvider, type, new FailingSubmissionOutboxWriter());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await (Task<Result<EnterpriseRequestResponse>>)type.GetMethod("SubmitAsync")!.Invoke(service, [draft.Id, draft.CreatedById, ct])!);
        tenant.Clear();
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        Assert.AreEqual("Draft", await connection.ExecuteScalarAsync<string>("SELECT Status FROM demo_enterprise_request_enterprise_request WHERE Id = @Id", new { draft.Id }));
        Assert.AreEqual(draft.Version, await connection.ExecuteScalarAsync<long>("SELECT Version FROM demo_enterprise_request_enterprise_request WHERE Id = @Id", new { draft.Id }));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_approval_submission WHERE RequestId = @Id", new { draft.Id }));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE TenantId = @TenantId AND MessageType = @MessageType",
            new { draft.TenantId, MessageType = EnterpriseRequestApprovalSubmittedIntegrationEvent.EventType }));
    }

    private sealed class FailingSubmissionOutboxWriter : IOutboxWriter
    {
        public Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
            string eventType, int schemaVersion, TEvent payload, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("test.outbox_write_failed");
        public Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
            string eventType, int schemaVersion, TEvent payload, Full.NET.Messaging.Abstractions.IntegrationEventMetadata metadata,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("test.outbox_write_failed");
    }

    private sealed class LoseFinalReceiptOnceExecutor(ICommandExecutor inner) : ICommandExecutor
    {
        private bool lost;
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (!lost && statement.Name == "enterprise_request.approval.mark_final")
            {
                lost = true;
                return Task.FromResult(0);
            }
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}
