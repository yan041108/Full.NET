using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowCollaborationSourceProcessor
{
    private async Task<bool> ApplyCancellationAsync(WorkflowCollaborationAdmissionRecord admission, CancellationToken token)
    {
        var saved = await transaction.ExecuteResultAsync(async inner =>
        {
            var state = await LockAsync(admission, "cancelled", "cancel_requested", inner).ConfigureAwait(false);
            if (state is null || state.Call.StatusKey is not ("requested" or "accepted" or "result_ready" or "cancelling")) return Result<bool>.Success(false);
            // 新尝试已关闭旧代次时，只结清旧调用；不能暂停或推进当前新尝试。
            var waiting = state.StepRevision is not null && (state.Root.StatusKey is "active" or "suspended");
            if (await commands.ExecuteAsync(WorkflowCollaborationControlSql.CancelSourceCall,
                    Params(state, ("StatusKey", waiting ? "denied" : "cancelled")), inner).ConfigureAwait(false) != 1 ||
                !await FinishAsync(state, "cancelled", inner).ConfigureAwait(false) || waiting && !await SuspendAsync(state, inner).ConfigureAwait(false)) return Invalid();
            await AuditAsync(state, "closed", waiting ? "workflow.collaboration.closed_" + state.Body.OutcomeKey : null, inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return saved.IsSuccess && saved.Value;
    }
}
