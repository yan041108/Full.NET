using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.DataApproval.Serialization;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;

/// <summary>先在事务外核对真实工作流证明并调用所有者，再在审批事务中持久回执和Outbox。</summary>
internal sealed class DataApprovalBusinessCommandSink(IQueryExecutor query, ICommandExecutor commands,
    ICommandTransaction transaction, ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOutboxWriter outbox,
    IWorkflowBusinessCommandProofReader proofs, IEnumerable<IDataApprovalScenarioCommandHandler> handlers) : IWorkflowBusinessCommandSink
{
    public string OwnerKey => "data_approval";
    internal static string? ActionFor(string scenario) => scenario switch
    {
        DataApprovalScenarioKeys.SerialRuleHostUpdate => "data_approval.apply_serial_rule_update",
        DataApprovalScenarioKeys.SerialRuleHostDisable => "data_approval.apply_serial_rule_disable",
        _ => null
    };

    public async Task HandleAsync(IntegrationEventContext context, WorkflowBusinessCommandRequestedIntegrationEvent value, CancellationToken token)
    {
        var binding = value.Binding;
        // 首批真实场景只有Host；不能将无租户消息作为跨租户管理员权限使用。
        if (!tenant.IsHost || context.TenantId is not null || binding.OwnerKey != OwnerKey || binding.CallId == Guid.Empty ||
            binding.InstanceId == Guid.Empty || value.Generation is < 1 or > 1000 || value.PhaseKey is not ("execute" or "compensate") ||
            WorkflowBusinessCommandHash.Compute(binding) != value.InputHash || !Guid.TryParse(binding.BusinessId, out var requestId))
            throw new InvalidOperationException("data_approvals.command_invalid");
        var proof = await proofs.FindAsync(binding.CallId, binding.InstanceId, value.InputHash, token).ConfigureAwait(false);
        if (proof is null || proof.Binding != binding || proof.InputHash != value.InputHash || value.Generation > proof.Generation)
            throw new InvalidOperationException("data_approvals.command_proof_invalid");
        if (value.Generation < proof.Generation) return;
        var row = await query.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(DataApprovalSql.FindRequestById,
            DataApprovalSqlParameters.Create(("Id", requestId), ("TenantScopeKey", "host")), token).ConfigureAwait(false);
        if (row?.WorkflowInstanceId is null) throw new InvalidOperationException("data_approvals.workflow_link_pending");
        if (row.WorkflowInstanceId != binding.InstanceId || row.WorkflowDefinitionVersionId != binding.DefinitionVersionId ||
            row.SubmittedByUserId != binding.RequestedByUserId || row.TenantId is not null || row.TenantScopeKey != "host" ||
            ActionFor(row.ScenarioKey) != binding.ActionKey || binding.ActionVersion != 1 ||
            DataApprovalScenarioCatalog.Find(row.ScenarioKey)?.WorkflowBusinessType != binding.BusinessType)
            throw new InvalidOperationException("data_approvals.command_binding_invalid");
        var known = await FindAsync(value, token).ConfigureAwait(false);
        if (known is not null) { ValidateReceipt(known, value, row.Id); return; }
        if (proof.RootStatusKey == "completed") throw new InvalidOperationException("data_approvals.command_root_completed");
        var compensate = proof.RootStatusKey is "cancelled" or "rejected" || proof.CompensationRequested;
        if (value.PhaseKey == "compensate" && !proof.CompensationRequested)
            throw new InvalidOperationException("data_approvals.command_compensation_not_requested");
        if (!compensate && (proof.RootStatusKey is not ("active" or "suspended") || row.StatusKey != DataApprovalStatusKeys.InReview))
            throw new InvalidOperationException("data_approvals.command_not_admitted");
        var handler = handlers.SingleOrDefault(item => item.Supports(row.ScenarioKey)) ??
            throw new InvalidOperationException("data_approvals.command_handler_unavailable");
        var command = new DataApprovalScenarioCommand(binding.CallId, row.Id, row.ScenarioKey, row.TargetEntityId, row.AfterSnapshotJson, row.SubmittedByUserId);
        var effect = compensate ? await handler.CompensateAsync(command, token).ConfigureAwait(false) : await handler.ApplyAsync(command, token).ConfigureAwait(false);
        // 所有者返回失败不能臆断已撤回。只有明确无效果的校验/版本失败才允许declined。
        var outcome = effect.IsSuccess ? effect.Value!.OutcomeKey :
            !compensate && IsDefinitiveNoEffect(effect.Error!) ? "declined" : "unknown";
        if (effect.IsSuccess && (effect.Value!.CommandId != binding.CallId || effect.Value.InputHash.Length != 64 ||
            effect.Value.OutcomeKey is not ("applied" or "cancelled" or "compensated")))
            throw new InvalidOperationException("data_approvals.command_effect_invalid");
        var result = new WorkflowBusinessCommandResultIntegrationEvent(binding.CallId, binding.InstanceId, value.InputHash,
            value.PhaseKey, value.Generation, outcome, effect.Value?.InputHash, effect.Value?.AppliedVersion, effect.Value?.CompensatedVersion,
            effect.Error?.Code, clock.UtcNow);
        try
        {
            var saved = await transaction.ExecuteResultAsync(async inner =>
            {
                if (await FindAsync(value, inner).ConfigureAwait(false) is { } existing)
                { ValidateReceipt(existing, value, row.Id); return Result<bool>.Success(false); }
                var json = JsonSerializer.Serialize(result, DataApprovalJsonSerializerContext.Default.WorkflowBusinessCommandResultIntegrationEvent);
                if (await commands.ExecuteAsync(DataApprovalCommandSql.Insert, DataApprovalSqlParameters.Create(("Id", ids.NewId()),
                    ("RequestId", row.Id), ("InstanceId", binding.InstanceId), ("TenantScopeKey", "host"), ("CallId", binding.CallId),
                    ("PhaseKey", value.PhaseKey), ("Generation", value.Generation), ("InputHash", value.InputHash), ("OutcomeKey", outcome),
                    ("ResultJson", json), ("Now", clock.UtcNow)), inner).ConfigureAwait(false) != 1)
                    return Result<bool>.Failure(new Error("data_approvals.command_receipt_conflict", "Command receipt was not persisted.", ErrorType.Conflict));
                await outbox.AddAsync(WorkflowBusinessCommandIntegrationEventTypes.Result, 1, result,
                    IntegrationEventMetadata.Create(binding.InstanceId.ToString("D"), "fullnet.data_approval"), inner).ConfigureAwait(false);
                return Result<bool>.Success(true);
            }, token).ConfigureAwait(false);
            if (!saved.IsSuccess) throw new InvalidOperationException("data_approvals.command_receipt_conflict");
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            var existing = await FindAsync(value, token).ConfigureAwait(false);
            if (existing is null) throw;
            ValidateReceipt(existing, value, row.Id);
        }
    }

    private static bool IsDefinitiveNoEffect(Error error) => !error.Code.Contains("approval_command", StringComparison.Ordinal) &&
        (error.Type is ErrorType.Validation or ErrorType.NotFound or ErrorType.BusinessRule || error.Code == "serial_numbers.rule.version_conflict");
    private Task<DataApprovalCommandReceiptRecord?> FindAsync(WorkflowBusinessCommandRequestedIntegrationEvent value, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<DataApprovalCommandReceiptRecord>(DataApprovalCommandSql.Find, DataApprovalSqlParameters.Create(
            ("CallId", value.Binding.CallId), ("PhaseKey", value.PhaseKey), ("Generation", value.Generation), ("TenantScopeKey", "host")), token);
    private static void ValidateReceipt(DataApprovalCommandReceiptRecord row, WorkflowBusinessCommandRequestedIntegrationEvent value, Guid requestId)
    {
        if (row.InputHash != value.InputHash || row.RequestId != requestId || row.InstanceId != value.Binding.InstanceId)
            throw new InvalidOperationException("data_approvals.command_receipt_invalid");
    }
}
