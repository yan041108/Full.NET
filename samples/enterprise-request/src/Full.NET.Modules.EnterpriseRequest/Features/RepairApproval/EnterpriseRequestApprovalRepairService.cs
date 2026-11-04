using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.EnterpriseRequest.Features.RepairApproval;

/// <summary>申请恢复只接受原启动证据；补绑定与终态对账由申请本地事务负责。</summary>
internal sealed class EnterpriseRequestApprovalRepairService(
    EnterpriseRequestQueryService requests, IQueryExecutor queries, ICommandExecutor commands, ICurrentTenant tenant,
    IOrganizationOwnedEntityWriteAuthorizer authorizer, IWorkflowBusinessStartProofDirectory directory,
    ICommandTransaction transaction, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<bool>> RepairAsync(Guid id, RepairEnterpriseRequestApprovalRequest request,
        Guid actor, bool superAdministrator, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenant.IsHost || !tenant.IsAvailable || tenant.Id is not { } tenantId || tenantId == Guid.Empty || actor == Guid.Empty)
            return Fail("enterprise_request.tenant_context_required", ErrorType.Forbidden);
        if (id == Guid.Empty || request.WorkflowInstanceId == Guid.Empty || request.ExpectedVersion is < 2 or long.MaxValue ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500 || request.Reason.Any(char.IsControl))
            return Fail("enterprise_request.approval_repair_invalid", ErrorType.Validation);
        var read = await requests.GetByIdAsync(id, actor, superAdministrator, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) return Result<bool>.Failure(read.Error!);
        var row = read.Value!;
        if (row.Id != id || row.TenantId != tenantId || row.IsDeleted) return Fail(EnterpriseRequestErrorCodes.NotFound, ErrorType.NotFound);
        var access = await authorizer.EnsureCanWriteAsync(tenantId, row.OrganizationUnitId, actor, cancellationToken).ConfigureAwait(false);
        if (!access.IsSuccess || !access.Value) return Fail(OrganizationErrorCodes.WriteAccessDenied, ErrorType.Forbidden);
        var binding = await ReadBindingAsync(id, cancellationToken).ConfigureAwait(false);
        if (binding is not null && !Matches(binding, row, request.WorkflowInstanceId)) return Conflict();
        // 已封存的同一修复允许响应丢失重放，原回执和恢复原因不再覆盖。
        if (binding?.FinalStatus is { } final && EnterpriseRequestStatusTransition.IsTerminal(final) && row.Status == final &&
            row.Version == binding.RequestVersion + 1 && request.ExpectedVersion == binding.RequestVersion &&
            binding.CompletedAtUtc is not null && binding.LastMessageId is { } messageId && messageId != Guid.Empty)
            return Result<bool>.Success(true);
        if (row.Status != EnterpriseRequestStatusKeys.Submitted || row.Version != request.ExpectedVersion ||
            (binding is not null && (binding.RequestVersion != row.Version || binding.FinalStatus is not null))) return Conflict();
        var proof = await directory.FindAsync(new(request.WorkflowInstanceId, EnterpriseRequestWorkflowConstants.BusinessType,
            id.ToString("D"), "{}", $"submit:{id:D}:{row.Version - 1}"), cancellationToken).ConfigureAwait(false);
        if (proof is null || proof.InstanceId != request.WorkflowInstanceId || proof.TenantId != tenantId ||
            proof.BusinessType != EnterpriseRequestWorkflowConstants.BusinessType || proof.BusinessId != id.ToString("D") ||
            proof.DefinitionVersionId == Guid.Empty || proof.BusinessTitle != row.Title || proof.StartedById == Guid.Empty ||
            proof.StartedById != (binding?.SubmittedById ?? row.UpdatedById) ||
            (binding is not null && proof.DefinitionVersionId != binding.WorkflowDefinitionVersionId))
            return Fail("enterprise_request.approval_start_proof_missing", ErrorType.Conflict);
        var target = proof.StatusKey switch { "completed" => EnterpriseRequestStatusKeys.Approved,
            "rejected" => EnterpriseRequestStatusKeys.Rejected, "cancelled" => EnterpriseRequestStatusKeys.Cancelled, _ => null };
        if ((target is null && proof.StatusKey is not ("active" or "suspended")) ||
            (target is not null && (proof.CompletedAtUtc is null || proof.CompletedAtUtc < proof.StartedAtUtc))) return Conflict();
        if (binding?.StartedAtUtc is not null && target is null) return Result<bool>.Success(true);
        var operationId = ids.NewId(); var now = clock.UtcNow;
        return await transaction.ExecuteResultAsync(async token =>
        {
            var lockSql = databaseOptions.Value.Provider switch {
                DatabaseProvider.SqlServer => EnterpriseRequestApprovalRepairSql.LockParentSqlServer,
                DatabaseProvider.MySql => EnterpriseRequestApprovalRepairSql.LockParentMySql,
                _ => throw new InvalidOperationException("Unsupported database provider.")
            };
            if (await queries.QuerySingleOrDefaultAsync<Guid?>(lockSql,
                EnterpriseRequestApprovalSql.Parameters(("Id", id), ("ExpectedVersion", row.Version)), token).ConfigureAwait(false) != id)
                return Conflict();
            // 父行锁串行化补绑定；锁内复查只读本模块，不把跨模块证据读取放进事务。
            var current = await ReadBindingAsync(id, token).ConfigureAwait(false);
            if (current is not null && (!Matches(current, row, proof.InstanceId) || current.RequestVersion != row.Version ||
                current.WorkflowDefinitionVersionId != proof.DefinitionVersionId || current.SubmittedById != proof.StartedById || current.FinalStatus is not null))
                return Conflict();
            // 并发请求可能已补完启动回执；锁内无操作成功，不能追加记录或改变业务元数据。
            if (current?.StartedAtUtc is not null && target is null) return Result<bool>.Success(true);
            var submissionId = current?.Id ?? ids.NewId();
            if (current is null && await commands.ExecuteAsync(EnterpriseRequestApprovalSql.Insert,
                EnterpriseRequestApprovalSql.Parameters(("Id", submissionId), ("TenantId", tenantId), ("RequestId", id), ("RequestVersion", row.Version),
                    ("WorkflowDefinitionVersionId", proof.DefinitionVersionId), ("WorkflowInstanceId", proof.InstanceId),
                    ("SubmittedById", proof.StartedById), ("OrganizationUnitId", row.OrganizationUnitId), ("BusinessTitle", row.Title),
                    ("CreatedAtUtc", now)), token).ConfigureAwait(false) != 1) return Conflict();
            if (current?.StartedAtUtc is null)
            {
                if (await commands.ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted,
                    EnterpriseRequestApprovalSql.Parameters(("Id", submissionId), ("WorkflowInstanceId", proof.InstanceId), ("StartedAtUtc", proof.StartedAtUtc)), token).ConfigureAwait(false) != 1)
                {
                    var started = await ReadBindingAsync(id, token).ConfigureAwait(false);
                    if (started?.Id != submissionId || started.StartedAtUtc is null) return Conflict();
                }
            }
            if (target is not null)
            {
                if (await commands.ExecuteAsync(EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
                    EnterpriseRequestApprovalSql.Parameters(("Id", id), ("Status", target), ("UpdatedAtUtc", now),
                        ("ExpectedStatus", EnterpriseRequestStatusKeys.Submitted), ("ExpectedVersion", row.Version)), token).ConfigureAwait(false) != 1) return Conflict();
                // 对账操作 UUID 与持久化恢复记录关联，不伪装成原 Workflow 事件，也不补发通知。
                if (await commands.ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal,
                    EnterpriseRequestApprovalSql.Parameters(("Id", submissionId), ("WorkflowInstanceId", proof.InstanceId), ("FinalStatus", target),
                        ("CompletedAtUtc", now), ("LastMessageId", operationId)), token).ConfigureAwait(false) != 1) return Conflict();
            }
            var audit = await commands.ExecuteAsync(EnterpriseRequestApprovalRepairSql.InsertRepair,
                EnterpriseRequestApprovalSql.Parameters(("Id", operationId), ("TenantId", tenantId), ("RequestId", id), ("WorkflowInstanceId", proof.InstanceId),
                    ("RequestVersion", row.Version), ("KindKey", current is null ? "binding" : target is null ? "start_receipt" : "reconcile"), ("WorkflowStatusKey", proof.StatusKey),
                    ("ActorUserId", actor), ("Reason", request.Reason.Trim()), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
            return audit == 1 ? Result<bool>.Success(true) : Conflict();
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<EnterpriseRequestApprovalSubmission?> ReadBindingAsync(Guid id, CancellationToken ct) =>
        queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest,
            EnterpriseRequestApprovalSql.Parameters(("RequestId", id)), ct);
    private static bool Matches(EnterpriseRequestApprovalSubmission binding, EnterpriseRequestResponse row, Guid instance) =>
        binding.Id != Guid.Empty && binding.TenantId == row.TenantId && binding.RequestId == row.Id &&
        binding.OrganizationUnitId == row.OrganizationUnitId && binding.WorkflowInstanceId == instance &&
        binding.BusinessTitle == row.Title && binding.WorkflowDefinitionVersionId != Guid.Empty;
    private static Result<bool> Conflict() => Fail(EnterpriseRequestErrorCodes.VersionConflict, ErrorType.Conflict);
    private static Result<bool> Fail(string code, ErrorType type) => Result<bool>.Failure(new Error(code,
        "The approval recovery could not be verified. Refresh the request and check the original workflow start evidence.", type));
}
