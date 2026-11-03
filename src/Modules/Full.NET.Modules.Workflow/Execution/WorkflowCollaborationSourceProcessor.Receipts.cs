using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowCollaborationSourceProcessor
{
    private async Task<bool> FinishAsync(SourceState state, string nextAdmissionStatus, CancellationToken token)
    {
        var parameters = Params(state, ("AdmissionStatus", state.Admission.StatusKey), ("NextAdmissionStatus", nextAdmissionStatus));
        return await commands.ExecuteAsync(WorkflowCollaborationControlSql.Apply, parameters, token).ConfigureAwait(false) == 1 &&
            await commands.ExecuteAsync(WorkflowCollaborationControlSql.ReleaseBudget, parameters, token).ConfigureAwait(false) == 1 &&
            await commands.ExecuteAsync(WorkflowCollaborationControlSql.FinishAdmission, parameters, token).ConfigureAwait(false) == 1;
    }

    private async Task<bool> SuspendAsync(SourceState state, CancellationToken token) => state.Root.StatusKey != "active" ||
        await commands.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision, P(("Id", state.Root.Id), ("TenantScopeKey", state.Root.TenantScopeKey),
            ("Revision", state.Root.Revision)), token).ConfigureAwait(false) == 1;

    private async Task AuditAsync(SourceState state, string phase, string? error, CancellationToken token)
    {
        var detail = WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("nodeTypeKey", "process.collaboration"); writer.WriteString("phaseKey", phase);
            writer.WriteString("controlId", state.Control.Id); writer.WriteString("targetOutcomeKey", state.Body.OutcomeKey);
            if (state.Body.TargetInstanceId is { } target) writer.WriteString("targetInstanceId", target); else writer.WriteNull("targetInstanceId");
            writer.WriteNumber("inputFormRevision", state.Binding.InputFormRevision); writer.WriteNumber("unusedActivities", state.Body.UnusedActivities);
            if (error is not null) writer.WriteString("errorCode", error); writer.WriteEndObject();
        }).CanonicalJson;
        var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            P(("Id", ids.NewId()), ("TenantId", state.Root.TenantId), ("ScopeKey", state.Root.ScopeKey), ("InstanceId", state.Root.Id),
                ("OperationKey", "activity.execute"), ("ActorUserId", null), ("ResourceTypeKey", "step"), ("ResourceId", state.Call.Id),
                ("OutcomeKey", error is null ? "succeeded" : "failed"), ("DetailJson", detail), ("CorrelationJson", correlation), ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            P(("Id", ids.NewId()), ("InstanceId", state.Root.Id), ("StepId", state.Call.Id), ("TransitionKey", "node.collaboration." + phase),
                ("FromStatusKey", state.Root.StatusKey), ("ToStatusKey", error is null ? state.Root.StatusKey : "suspended"),
                ("IdempotencyKey", "workflow-control-" + state.Control.Id.ToString("N")), ("Summary", error ?? state.Control.BodyHash),
                ("CorrelationJson", correlation), ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
    }
}
