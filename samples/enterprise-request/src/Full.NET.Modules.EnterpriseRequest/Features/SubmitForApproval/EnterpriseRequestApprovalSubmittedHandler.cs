using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;

/// <summary>执行已获提交授权的持久化意图，业务事务外启动固定实例并支持崩溃重放。</summary>
/// <remarks>提交权限与会话在 API 提交时校验；后台不复用原会话，提交后的权限变更不撤销已提交命令。
/// 租户与机构归属仍须有效；本处理器不接受普通 HTTP 请求提供的 actor 或租户。</remarks>
internal sealed class EnterpriseRequestApprovalSubmittedHandler(
    IIntegrationEventSerializer serializer,
    IQueryExecutor queries,
    ICommandExecutor commands,
    IDataTransactionState transactionState,
    ICurrentTenantContextWriter tenantWriter,
    IActiveTenantContextResolver tenantResolver,
    IOrganizationOwnedEntityWriteAuthorizer authorizer,
    IWorkflowInstanceStarter starter,
    IClock clock) : IIntegrationEventHandler
{
    public string EventType => EnterpriseRequestApprovalSubmittedIntegrationEvent.EventType;
    public int SchemaVersion => EnterpriseRequestApprovalSubmittedIntegrationEvent.SchemaVersion;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("enterprise_request.event_context_required");

    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        // 停止信号在入口生效，不能依赖后续数据或契约端口才终止当前投递。
        cancellationToken.ThrowIfCancellationRequested();
        // 现阶段使用 Legacy Outbox；禁止在 Inbox 本地事务内调用 Workflow 写端口。
        if (transactionState.HasTransaction || context.TenantId is not { } tenantId || tenantId == Guid.Empty ||
            context.MessageId == Guid.Empty || context.MessageType != EventType || context.SchemaVersion != SchemaVersion)
            throw new InvalidOperationException("enterprise_request.invalid_start_delivery");
        var intent = serializer.Deserialize<EnterpriseRequestApprovalSubmittedIntegrationEvent>(payload);
        if (intent is null || intent.RequestId == Guid.Empty || intent.SubmissionId == Guid.Empty)
            throw new InvalidOperationException("enterprise_request.invalid_submission");
        var tenant = await tenantResolver.ResolveActiveByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null || tenant.Id != tenantId)
            throw new InvalidOperationException("enterprise_request.tenant_unavailable");
        using var scope = new EnterpriseRequestEventTenantScope(tenantWriter, tenant);
        var submission = await queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(
            EnterpriseRequestApprovalSql.FindByRequest, EnterpriseRequestApprovalSql.Parameters(("RequestId", intent.RequestId)), cancellationToken).ConfigureAwait(false);
        if (submission is null || submission.Id != intent.SubmissionId || submission.TenantId != tenantId ||
            submission.RequestId != intent.RequestId || submission.WorkflowInstanceId == Guid.Empty)
            throw new InvalidOperationException("enterprise_request.submission_mismatch");
        if (submission.StartedAtUtc is not null) return;

        var row = await queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(EnterpriseRequestSql.FindByIdStatement,
            EnterpriseRequestApprovalSql.Parameters(("Id", intent.RequestId)), cancellationToken).ConfigureAwait(false);
        if (row is null || row.Id != submission.RequestId || row.TenantId != tenantId || row.IsDeleted ||
            row.OrganizationUnitId != submission.OrganizationUnitId ||
            (submission.FinalStatus is null && (row.Status != EnterpriseRequestStatusKeys.Submitted || row.Version != submission.RequestVersion)))
            throw new InvalidOperationException("enterprise_request.submitted_snapshot_mismatch");
        var authorization = await authorizer.EnsureCanWriteAsync(tenantId, submission.OrganizationUnitId,
            submission.SubmittedById, cancellationToken).ConfigureAwait(false);
        if (!authorization.IsSuccess || !authorization.Value)
            throw new InvalidOperationException("enterprise_request.submit_access_revoked");
        var start = await starter.StartAsync(submission.SubmittedById,
            new StartWorkflowInstanceCommand(submission.WorkflowDefinitionVersionId,
                EnterpriseRequestWorkflowConstants.BusinessType, submission.RequestId.ToString("D"), "{}",
                $"submit:{submission.RequestId:D}:{submission.RequestVersion - 1}", submission.BusinessTitle,
                submission.WorkflowInstanceId), cancellationToken).ConfigureAwait(false);
        if (!start.IsSuccess || start.Value?.InstanceId != submission.WorkflowInstanceId)
            throw new InvalidOperationException(EnterpriseRequestWorkflowErrorCodes.WorkflowStartFailed);
        var affected = await commands.ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted,
            EnterpriseRequestApprovalSql.Parameters(("Id", submission.Id), ("WorkflowInstanceId", submission.WorkflowInstanceId),
                ("StartedAtUtc", clock.UtcNow)), cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            var latest = await queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest,
                EnterpriseRequestApprovalSql.Parameters(("RequestId", submission.RequestId)), cancellationToken).ConfigureAwait(false);
            if (latest?.Id != submission.Id || latest.WorkflowInstanceId != submission.WorkflowInstanceId || latest.StartedAtUtc is null)
                throw new InvalidOperationException("enterprise_request.start_receipt_conflict");
        }
    }
}
