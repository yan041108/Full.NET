using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowCollaborationSourceProcessor
{
    private async Task<bool> ConfirmCreatedAsync(WorkflowCollaborationAdmissionRecord admission, CancellationToken token)
    {
        var saved = await transaction.ExecuteResultAsync(async inner =>
        {
            var state = await LockAsync(admission, "created", "created", inner).ConfigureAwait(false);
            if (state is null || state.Call.StatusKey != "requested") return Result<bool>.Success(false);
            var parameters = Params(state);
            if (await commands.ExecuteAsync(WorkflowCollaborationRelaySql.MarkAccepted, parameters, inner).ConfigureAwait(false) != 1 ||
                await commands.ExecuteAsync(WorkflowCollaborationControlSql.Apply, parameters, inner).ConfigureAwait(false) != 1 ||
                await commands.ExecuteAsync(WorkflowCollaborationRelaySql.ConfirmAccepted, parameters, inner).ConfigureAwait(false) != 1) return Invalid();
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return saved.IsSuccess && saved.Value;
    }
}
