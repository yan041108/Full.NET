using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Execution.Connectors;

/// <summary>网络结果已持久保存后才准备文件和接纳输出，提交失败不会重新发送HTTP。</summary>
internal sealed partial class WorkflowConnectorProcessor
{
    private async Task<bool> ApplyAsync(WorkflowConnectorCallRecord call, CancellationToken token)
    {
        var instance = await FindInstanceAsync(call, token).ConfigureAwait(false);
        if (!Trusted(instance, call) || instance!.StatusKey != "active") return false;
        using var tenantScope = new WorkflowExecutionTenantScope(tenants, instance.TenantId);
        var owner = ids.NewId().ToString("N");
        var claimed = await transaction.ExecuteResultAsync(async inner =>
        {
            var current = await FindInstanceAsync(call, inner).ConfigureAwait(false);
            if (!Trusted(current, call) || current!.StatusKey != "active" || !await LockAsync(current, inner).ConfigureAwait(false)) return Invalid<bool>();
            return await commands.ExecuteAsync(WorkflowConnectorSql.ClaimApply,
                Parameters(("Id", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", call.TenantScopeKey), ("Revision", call.Revision),
                    ("Owner", owner), ("Expires", clock.UtcNow.AddSeconds(Math.Max(120, options.Value.LeaseSeconds))), ("Now", clock.UtcNow)), inner).ConfigureAwait(false) == 1
                ? Result<bool>.Success(true) : Invalid<bool>();
        }, token).ConfigureAwait(false);
        if (!claimed.IsSuccess) return false;
        var applying = call with { StatusKey = "applying", LeaseOwnerKey = owner, LeaseGeneration = call.LeaseGeneration + 1,
            ApplyAttemptCount = call.ApplyAttemptCount + 1, Revision = call.Revision + 1 };
        if (applying.ApplyAttemptCount > Math.Min(8, options.Value.MaxAttempts))
            return await FaultApplyAsync(applying, "workflow.connector.apply_exhausted", token).ConfigureAwait(false);
        var runtime = await LoadRuntimeAsync(instance, applying, token).ConfigureAwait(false);
        var attempt = applying.SelectedAttemptId is { } attemptId ? await query.QuerySingleOrDefaultAsync<WorkflowConnectorAttemptRecord>(
            WorkflowConnectorSql.FindAttempt, Parameters(("Id", attemptId), ("CallId", applying.Id), ("TenantScopeKey", applying.TenantScopeKey)), token).ConfigureAwait(false) : null;
        if (runtime is null || attempt is not { StatusKey: "received", OutcomeKey: "succeeded", ResponseJson: { } response } ||
            attempt.RequestHash != applying.RequestHash || Hash(response) != attempt.ResponseHash ||
            !runtime.Policy.TryMapOutput(runtime.Values, response, out var output))
            return await FaultApplyAsync(applying, "workflow.connector.result_invalid", token).ConfigureAwait(false);
        var prepared = await outputs.PrepareAsync(instance, Input(applying), runtime.Schema, runtime.Values, output!, runtime.Policy.CanRead, token).ConfigureAwait(false);
        if (prepared is null) return await FaultApplyAsync(applying, "workflow.connector.input_or_attachment_invalid", token).ConfigureAwait(false);
        try
        {
            var result = await transaction.ExecuteResultAsync(async inner =>
            {
                var current = await FindInstanceAsync(applying, inner).ConfigureAwait(false);
                if (!Trusted(current, applying) || current!.StatusKey != "active" || !await LockAsync(current, inner).ConfigureAwait(false)) return Invalid<bool>();
                if (!await outputs.CommitAsync(current, Input(applying), runtime.Schema, runtime.Values, prepared, inner).ConfigureAwait(false)) return Invalid<bool>();
                var now = clock.UtcNow; var revision = applying.InputFormRevision + (prepared.Output.Matched ? 1 : 0);
                if (await commands.ExecuteAsync(WorkflowSql.CompleteStepWithRevision,
                        Parameters(("Id", applying.Id), ("InstanceId", applying.InstanceId), ("Revision", 1L), ("StatusKey", "completed"), ("CompletedAtUtc", now)), inner).ConfigureAwait(false) != 1)
                    return Invalid<bool>();
                await commands.ExecuteAsync(WorkflowFieldActivitySql.InsertResult,
                    Parameters(("Id", applying.Id), ("InstanceId", applying.InstanceId), ("TenantScopeKey", applying.TenantScopeKey),
                        ("InputFormRevision", applying.InputFormRevision), ("OutputFormRevision", revision), ("InputJson", applying.InputJson), ("InputHash", applying.InputHash),
                        ("OutputJson", prepared.OutputJson), ("OutputHash", Hash(prepared.OutputJson)), ("PatchJson", Serialize(prepared.Output.Patch)),
                        ("OutcomeKey", prepared.Output.Matched ? "applied" : "skipped"), ("Now", now)), inner).ConfigureAwait(false);
                var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
                    Parameters(("InstanceId", applying.InstanceId)), inner).ConfigureAwait(false);
                await scheduler.EnqueueAsync(current, applying.ContinuationNodeKey, prepared.Output.Values, revision, sequence,
                    applying.ParallelJoinId, applying.ParallelBranchKey, now, inner).ConfigureAwait(false);
                if (await commands.ExecuteAsync(WorkflowConnectorSql.FinishApply,
                        Parameters(("Id", applying.Id), ("InstanceId", applying.InstanceId), ("TenantScopeKey", applying.TenantScopeKey),
                            ("Generation", applying.LeaseGeneration), ("Owner", owner), ("AttemptId", applying.SelectedAttemptId), ("FormRevision", revision), ("Now", now)), inner).ConfigureAwait(false) != 1 ||
                    await commands.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
                        Parameters(("Id", current.Id), ("TenantScopeKey", current.TenantScopeKey), ("Revision", current.Revision)), inner).ConfigureAwait(false) != 1) return Invalid<bool>();
                await AuditAsync(current, applying, "applied", applying.SelectedAttemptId, null, inner).ConfigureAwait(false);
                return Result<bool>.Success(true);
            }, token).ConfigureAwait(false);
            if (!result.IsSuccess) _ = await FaultApplyAsync(applying, "workflow.connector.output_conflict", token).ConfigureAwait(false);
            return true;
        }
        finally { await outputs.FinishAsync(prepared, CancellationToken.None).ConfigureAwait(false); }
    }

    private async Task<bool> FaultApplyAsync(WorkflowConnectorCallRecord call, string error, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner =>
        {
            var instance = await FindInstanceAsync(call, inner).ConfigureAwait(false);
            if (!Trusted(instance, call) || instance!.StatusKey != "active" || !await LockAsync(instance, inner).ConfigureAwait(false)) return Invalid<bool>();
            if (await commands.ExecuteAsync(WorkflowConnectorSql.FailApply,
                    Parameters(("Id", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", call.TenantScopeKey),
                        ("Generation", call.LeaseGeneration), ("Owner", call.LeaseOwnerKey), ("ErrorKey", error), ("Now", clock.UtcNow)), inner).ConfigureAwait(false) != 1 ||
                !await SuspendAsync(instance, inner).ConfigureAwait(false)) return Invalid<bool>();
            await AuditAsync(instance, call, "failed", call.SelectedAttemptId, error, inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private static string Serialize(IReadOnlyDictionary<string, JsonElement> values) => JsonSerializer.Serialize(
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
}
