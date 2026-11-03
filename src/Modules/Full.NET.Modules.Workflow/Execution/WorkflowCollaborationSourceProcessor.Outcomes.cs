using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowCollaborationSourceProcessor
{
    private async Task<bool> ApplyCompletionAsync(WorkflowCollaborationAdmissionRecord admission, CancellationToken token)
    {
        var before = await ReadAsync(admission, "completed", token).ConfigureAwait(false);
        if (before is null || before.Root.StatusKey != "active" || before.Call.StatusKey != "accepted") return false;
        if (before.Body.OutcomeKey != "completed") return await DenyAsync(admission, "workflow.collaboration.target_" + before.Body.OutcomeKey, token).ConfigureAwait(false);
        var scope = WorkflowManagementScope.Resolve(tenant);
        var grant = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
            P(("Id", before.Binding.GrantId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (grant is null || grant.PolicyHash != before.Binding.PolicyHash || !WorkflowCollaborationGrantPolicy.TryAuthorize(grant,
                scope.TenantScopeKey, false, before.Binding.GrantRevision, clock.UtcNow, out _, out var accepted)) return false;
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            P(("DefinitionVersionId", before.Root.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        WorkflowFormSchema? schema; Dictionary<string, JsonElement>? original;
        try
        {
            schema = asset is null ? null : JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            original = JsonSerializer.Deserialize(before.Call.InputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        }
        catch (JsonException) { schema = null; original = null; }
        if (schema is null || original is null || asset!.FormVersionId != before.Binding.SourceFormVersionId)
            return await DenyAsync(admission, "workflow.collaboration.output_invalid", token).ConfigureAwait(false);
        var snapshot = await outputs.ReadSnapshotAsync(before.Root, before.Binding.ParallelJoinId, before.Binding.ParallelBranchKey, schema, token).ConfigureAwait(false);
        if (snapshot is null) return false;
        var files = await remoteFiles.PrepareSourceResultAsync(admission, accepted!, schema, before.Body.ProjectionJson, token).ConfigureAwait(false);
        if (files is null) return await DenyAsync(admission, "workflow.collaboration.output_invalid", token).ConfigureAwait(false);
        if (!WorkflowCollaborationResultProjection.TryMerge(schema, accepted!, original, snapshot.Values, files.ProjectionJson, out var output, out var error, verifiedFiles: true))
            return await DenyAsync(admission, error, token).ConfigureAwait(false);
        var input = new WorkflowAutomaticInput(before.Call.Id, snapshot.Json, snapshot.Hash, snapshot.FormRevision, before.Binding.ParallelJoinId, before.Binding.ParallelBranchKey);
        var prepared = await outputs.PrepareAsync(before.Root, input, schema, snapshot.Values, output!,
            key => accepted!.InputFields.ContainsKey(key) || accepted.OutputFields.Values.Contains(key, StringComparer.Ordinal), token, files.LocalReuse, files.Remote).ConfigureAwait(false);
        if (prepared is null) return await DenyAsync(admission, "workflow.collaboration.output_invalid", token).ConfigureAwait(false);
        try
        {
            var applied = await transaction.ExecuteResultAsync(async inner =>
            {
                var current = await LockAsync(admission, "completed", "active", inner).ConfigureAwait(false);
                if (current is null || current.Call.StatusKey != "accepted" || current.Control.Id != before.Control.Id || current.Control.BodyHash != before.Control.BodyHash)
                    return Result<bool>.Success(false);
                if (!await outputs.CommitAsync(current.Root, input, schema, snapshot.Values, prepared, inner).ConfigureAwait(false)) return Invalid();
                if (await commands.ExecuteAsync(WorkflowSql.CompleteStepWithRevision,
                        P(("Id", current.Call.Id), ("InstanceId", current.Root.Id), ("Revision", current.StepRevision),
                            ("StatusKey", "completed"), ("CompletedAtUtc", clock.UtcNow)), inner).ConfigureAwait(false) != 1 ||
                    await commands.ExecuteAsync(WorkflowCollaborationControlSql.CompleteSourceCall, Params(current, ("StatusKey", "completed")), inner).ConfigureAwait(false) != 1 ||
                    !await FinishAsync(current, "completed", inner).ConfigureAwait(false)) return Invalid();
                var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
                    P(("InstanceId", current.Root.Id)), inner).ConfigureAwait(false);
                await scheduler.EnqueueAsync(current.Root, current.Binding.ExecutionEpochId, current.Call.ContinuationNodeKey, prepared.Output.Values,
                    snapshot.FormRevision + (prepared.Output.Matched ? 1 : 0), sequence, current.Binding.ParallelJoinId, current.Binding.ParallelBranchKey,
                    clock.UtcNow, inner, sourceStepId: current.Call.Id).ConfigureAwait(false);
                if (await commands.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
                        P(("Id", current.Root.Id), ("TenantScopeKey", current.Root.TenantScopeKey), ("Revision", current.Root.Revision)), inner).ConfigureAwait(false) != 1) return Invalid();
                await AuditAsync(current, "applied", null, inner).ConfigureAwait(false);
                return Result<bool>.Success(true);
            }, token).ConfigureAwait(false);
            return applied.IsSuccess && applied.Value;
        }
        finally { await outputs.FinishAsync(prepared, CancellationToken.None).ConfigureAwait(false); }
    }

    private async Task<bool> DenyAsync(WorkflowCollaborationAdmissionRecord admission, string code, CancellationToken token)
    {
        var saved = await transaction.ExecuteResultAsync(async inner =>
        {
            var state = await LockAsync(admission, "completed", "active", inner).ConfigureAwait(false);
            if (state is null || state.Call.StatusKey != "accepted") return Result<bool>.Success(false);
            if (await commands.ExecuteAsync(WorkflowCollaborationControlSql.CompleteSourceCall, Params(state, ("StatusKey", "denied")), inner).ConfigureAwait(false) != 1 ||
                !await FinishAsync(state, "completed", inner).ConfigureAwait(false) || !await SuspendAsync(state, inner).ConfigureAwait(false)) return Invalid();
            await AuditAsync(state, "denied", code, inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return saved.IsSuccess && saved.Value;
    }
}
