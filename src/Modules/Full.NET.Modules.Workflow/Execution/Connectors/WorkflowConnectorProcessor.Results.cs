using System.Data.Common;
using Full.NET.Abstractions.Observability;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution.Connectors;

/// <summary>先持久保存原尝试结果，再有条件更新活动头；过期结果不获得新Worker的提交权限。</summary>
internal sealed partial class WorkflowConnectorProcessor
{
    private async Task ReceiveAsync(WorkflowConnectorCallRecord call, WorkflowBoundHttpConnector connector,
        WorkflowConnectorCallResult response, CancellationToken token)
    {
        var received = Result<bool>.Success(false);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                received = await transaction.ExecuteResultAsync(async inner =>
                {
                    var instance = await FindInstanceAsync(call, inner).ConfigureAwait(false);
                    if (!Trusted(instance, call) || !await LockAsync(instance!, inner).ConfigureAwait(false)) return Invalid<bool>();
                    if (await commands.ExecuteAsync(WorkflowConnectorSql.ReceiveAttempt, ResponseParameters(call, response), inner).ConfigureAwait(false) != 1)
                        return Result<bool>.Success(false);
                    var status = NextStatus(response.OutcomeKey, connector.Definition.SupportsIdempotency || response.ErrorKey == "workflow.connector.cancelled_before_send", call.AttemptCount, connector.Definition.MaximumAttempts);
                    var error = status == "outcome_unknown" && response.OutcomeKey == "retryable" ? "workflow.connector.retry_exhausted" : response.ErrorKey;
                    var captured = await commands.ExecuteAsync(WorkflowConnectorSql.CaptureResult,
                        Parameters(("Id", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", call.TenantScopeKey),
                            ("AttemptId", call.CurrentAttemptId), ("Generation", call.LeaseGeneration), ("Owner", call.LeaseOwnerKey),
                            ("OutcomeKey", response.OutcomeKey), ("NextStatusKey", status), ("ErrorKey", error), ("Now", clock.UtcNow),
                            ("DueAtUtc", NextDue(call.AttemptCount))), inner).ConfigureAwait(false);
                    if (captured == 1)
                    {
                        var current = await FindAsync(call.Id, call.TenantScopeKey, inner).ConfigureAwait(false);
                        if (current?.StatusKey is "outcome_unknown" or "failed" or "rejected" && !await SuspendAsync(instance!, inner).ConfigureAwait(false)) return Invalid<bool>();
                        await AuditAsync(instance!, call, current?.StatusKey ?? "received", call.CurrentAttemptId, error, inner).ConfigureAwait(false);
                    }
                    return Result<bool>.Success(true);
                }, token).ConfigureAwait(false);
                if (received.IsSuccess) return;
            }
            catch (DbException) when (attempt < 2) { }
        }
        // 不能因实例修订持续竞争丢掉原响应；此回退只追加原尝试，不写活动头或推进实例。
        await transaction.ExecuteResultAsync(async inner =>
        {
            await commands.ExecuteAsync(WorkflowConnectorSql.ReceiveAttempt, ResponseParameters(call, response), inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
    }

    private IReadOnlyDictionary<string, object?> ResponseParameters(WorkflowConnectorCallRecord call, WorkflowConnectorCallResult result) =>
        Parameters(("Id", call.Id), ("AttemptId", call.CurrentAttemptId), ("TenantScopeKey", call.TenantScopeKey),
            ("Generation", call.LeaseGeneration), ("Owner", call.LeaseOwnerKey), ("RequestHash", call.RequestHash),
            ("OutcomeKey", result.OutcomeKey), ("ResponseJson", result.ResponseJson), ("ResponseHash", result.ResponseJson is { } json ? Hash(json) : null),
            ("HttpStatusCode", result.HttpStatusCode), ("ErrorKey", result.ErrorKey), ("Now", clock.UtcNow));

    private async Task<bool> RecoverAsync(WorkflowConnectorCallRecord call, CancellationToken token)
    {
        var recovered = await transaction.ExecuteResultAsync(async inner =>
        {
            var instance = await FindInstanceAsync(call, inner).ConfigureAwait(false);
            if (!Trusted(instance, call) || !await LockAsync(instance!, inner).ConfigureAwait(false)) return Invalid<bool>();
            using var tenantScope = new WorkflowExecutionTenantScope(tenants, instance!.TenantId);
            var attempt = call.CurrentAttemptId is { } attemptId
                ? await query.QuerySingleOrDefaultAsync<WorkflowConnectorAttemptRecord>(WorkflowConnectorSql.FindAttempt,
                    Parameters(("Id", attemptId), ("CallId", call.Id), ("TenantScopeKey", call.TenantScopeKey)), inner).ConfigureAwait(false) : null;
            var bound = catalog.Find(call.TenantScopeKey, call.ConnectorKey, call.ConnectorVersion, call.ConnectorHash);
            var proof = attempt is { StatusKey: "received" } && attempt.LeaseGeneration == call.LeaseGeneration && attempt.RequestHash == call.RequestHash;
            var status = proof ? NextStatus(attempt!.OutcomeKey!, bound?.Definition.SupportsIdempotency == true || attempt.ErrorKey == "workflow.connector.cancelled_before_send", call.AttemptCount, bound?.Definition.MaximumAttempts ?? 1)
                : bound?.Definition.SupportsIdempotency == true && call.AttemptCount < bound.Definition.MaximumAttempts ? "retryable" : "outcome_unknown";
            var error = proof ? attempt!.ErrorKey : "workflow.connector.result_unconfirmed";
            if (await commands.ExecuteAsync(WorkflowConnectorSql.RecoverExpired,
                    Parameters(("Id", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", call.TenantScopeKey),
                        ("Generation", call.LeaseGeneration), ("Revision", call.Revision), ("Now", clock.UtcNow), ("DueAtUtc", NextDue(call.AttemptCount)),
                        ("NextStatusKey", status), ("SelectedAttemptId", status == "result_ready" ? attempt!.Id : (Guid?)null), ("ErrorKey", error)), inner).ConfigureAwait(false) != 1)
                return Invalid<bool>();
            var current = await FindAsync(call.Id, call.TenantScopeKey, inner).ConfigureAwait(false);
            if (current?.StatusKey is "outcome_unknown" or "failed" or "rejected" && !await SuspendAsync(instance, inner).ConfigureAwait(false)) return Invalid<bool>();
            await AuditAsync(instance, call, current?.StatusKey ?? "recovered", call.CurrentAttemptId, error, inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return recovered.IsSuccess && recovered.Value;
    }

    private async Task<bool> FaultPendingAsync(WorkflowConnectorCallRecord call, string error, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner =>
        {
            var instance = await FindInstanceAsync(call, inner).ConfigureAwait(false);
            if (!Trusted(instance, call) || instance!.StatusKey != "active" || !await LockAsync(instance, inner).ConfigureAwait(false)) return Invalid<bool>();
            return await FailPendingCoreAsync(instance, call, call.AttemptCount == 0 ? "failed" : "outcome_unknown", error, inner).ConfigureAwait(false)
                ? Result<bool>.Success(true) : Invalid<bool>();
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private async Task<bool> FailPendingCoreAsync(WorkflowInstanceRecord instance, WorkflowConnectorCallRecord call, string status, string error, CancellationToken token)
    {
        if (await commands.ExecuteAsync(WorkflowConnectorSql.FailPending,
                Parameters(("Id", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", call.TenantScopeKey),
                    ("Revision", call.Revision), ("StatusKey", status), ("ErrorKey", error), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1 ||
            !await SuspendAsync(instance, token).ConfigureAwait(false)) return false;
        await AuditAsync(instance, call, status, call.CurrentAttemptId, error, token).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> SuspendAsync(WorkflowInstanceRecord instance, CancellationToken token) => instance.StatusKey != "active" ||
        await commands.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision,
            Parameters(("Id", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("Revision", instance.Revision)), token).ConfigureAwait(false) == 1;

    private async Task AuditAsync(WorkflowInstanceRecord instance, WorkflowConnectorCallRecord activation, string phase, Guid? attemptId, string? error, CancellationToken token)
    {
        var nodeTypeKey=activation.ModeKey=="async"?"integration.async_trigger":"integration.trigger";
        var detail = WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("nodeTypeKey", nodeTypeKey); writer.WriteString("phaseKey", phase);
            if (attemptId is { } id) writer.WriteString("attemptId", id);
            writer.WriteString("errorKey", error); writer.WriteEndObject();
        }).CanonicalJson;
        await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            Parameters(("Id", ids.NewId()), ("TenantId", instance.TenantId), ("ScopeKey", instance.ScopeKey), ("InstanceId", instance.Id),
                ("OperationKey", "activity.execute"), ("ActorUserId", null), ("ResourceTypeKey", "step"), ("ResourceId", activation.Id),
                ("OutcomeKey", error is null ? "succeeded" : "failed"), ("DetailJson", detail), ("CreatedAtUtc", clock.UtcNow),
                ("CorrelationJson", LogCorrelationContext.Capture().ToJson())), token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            Parameters(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", activation.Id),
                ("CorrelationJson", LogCorrelationContext.Capture().ToJson()), ("TransitionKey", $"node.{nodeTypeKey}.{phase}"),
                ("FromStatusKey", "active"), ("ToStatusKey", phase == "applied" ? "completed" : phase == "cancelled" ? "cancelled" : "active"),
                ("IdempotencyKey", null), ("Summary", error), ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
    }

    private DateTimeOffset NextDue(int attempts) => clock.UtcNow.AddSeconds(Math.Min(300, 5 * (1 << Math.Clamp(attempts - 1, 0, 6))));
    private static string NextStatus(string outcome, bool idempotent, int attempts, int maximum) => outcome switch
    {
        "succeeded" => "result_ready", "rejected" => "rejected", "failed" => "failed",
        "retryable" when idempotent && attempts < maximum => "retryable", _ => "outcome_unknown"
    };
}
