using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>唯一目标发布者；崩溃后从真实根重新发现事实，协议行与Outbox在目标自己的事务中共同提交。</summary>
internal sealed class WorkflowCollaborationControlPublisher(IQueryExecutor query, ICommandExecutor commands, ICommandTransaction transaction,
    ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOutboxWriter outbox, WorkflowRemoteFileService remoteFiles)
{
    internal async Task<bool> EnsureAsync(WorkflowCollaborationAdmissionRecord prepared, string kind, CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (scope.TenantScopeKey != prepared.TargetTenantScopeKey || !WorkflowCollaborationAdmissionProof.TryRead(prepared, out var proof)) return false;
        // Files签发必须先退出业务事务；随后在目标锁内复核同一固定表单快照。
        var preparedProjection = kind == "completed" ? await remoteFiles.PrepareTargetResultAsync(prepared, token).ConfigureAwait(false) : null;
        var saved = await transaction.ExecuteResultAsync(async inner =>
        {
            var parameters = P(("Id", prepared.Id), ("TenantScopeKey", scope.TenantScopeKey), ("BindingHash", prepared.BindingHash),
                ("TargetInstanceId", prepared.TargetInstanceId), ("KindKey", kind));
            if (prepared.TargetInstanceId is { } targetId && await commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                    P(("Id", targetId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false) != 1) return Invalid();
            if (kind == "cancelled" && await commands.ExecuteAsync(WorkflowCollaborationControlSql.LockClosing, parameters, inner).ConfigureAwait(false) != 1) return Invalid();
            var admission = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission, parameters, inner).ConfigureAwait(false);
            if (admission is null || admission.TargetInstanceId != prepared.TargetInstanceId || admission.BindingHash != prepared.BindingHash ||
                !WorkflowCollaborationAdmissionProof.TryRead(admission, out _)) return Invalid();
            var prior = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationControlRecord>(WorkflowCollaborationControlSql.Find, parameters, inner).ConfigureAwait(false);
            if (prior is not null) return WorkflowCollaborationControlPolicy.TryRead(prior, admission, out _) ? Result<bool>.Success(false) : Invalid();
            if (kind == "cancelled" ? admission.StatusKey != "cancel_requested" : admission.StatusKey is not ("created" or "active" or "cancel_requested")) return Invalid();
            var grant = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
                P(("Id", proof!.GrantId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false);
            if (grant is null || grant.PolicyHash != proof.PolicyHash || !WorkflowCollaborationGrantPolicy.TryReadAgreement(grant, out _, out var accepted)) return Invalid();
            WorkflowInstanceRecord? root = null;
            if (admission.TargetInstanceId is { } rootId)
            {
                root = await query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, P(("Id", rootId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false);
                var origin = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationOriginRecord>(WorkflowCollaborationRelaySql.FindOrigin,
                    P(("InstanceId", rootId), ("TenantScopeKey", scope.TenantScopeKey)), inner).ConfigureAwait(false);
                if (root is null || root.DefinitionVersionId != proof.TargetDefinitionVersionId || root.StartedById != accepted!.StarterUserId ||
                    root.BusinessType != WorkflowCollaborationStartContext.BusinessType || root.BusinessId != proof.CallId.ToString("D") ||
                    origin?.CallId != proof.CallId || origin.RootInstanceId != root.Id || origin.Depth != proof.Depth) return Invalid();
            }
            if (kind != "cancelled" && root is null) return Invalid();
            if (kind == "completed" && !await WorkflowCollaborationRuntimeGate.AllowsAsync(root!.Id, scope, null, clock.UtcNow,
                    query, inner, commands).ConfigureAwait(false)) return Invalid();
            var projection = "{}"; var unused = 0; var outcome = kind == "created" ? "created" : root?.StatusKey ?? "not_created";
            if (kind != "created")
            {
                if (root is not null)
                {
                    if (root.StatusKey is not ("completed" or "rejected" or "cancelled") ||
                        await query.QuerySingleOrDefaultAsync<int>(WorkflowCollaborationControlSql.CountUnsettled, parameters, inner).ConfigureAwait(false) != 0) return Result<bool>.Success(false);
                    var facts = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationBudgetFacts>(WorkflowCollaborationControlSql.ReadBudget, parameters, inner).ConfigureAwait(false);
                    if (facts is null || facts.InstanceCount is < 1 || facts.InstanceCount > proof.ReservedActivities || facts.InvalidBudgets != 0 ||
                        facts.UnusedActivities < 0 || facts.UnusedActivities > proof.ReservedActivities) return Invalid();
                    unused = facts.UnusedActivities;
                    if (kind == "completed" && root.StatusKey == "completed")
                    {
                        var projected = await ProjectAsync(root, accepted!, scope, preparedProjection, inner).ConfigureAwait(false);
                        if (projected is null) return Invalid();
                        projection = projected;
                    }
                }
                else unused = proof.ReservedActivities;
            }
            var controlId = ids.NewId(); var body = new WorkflowCollaborationControlBody(controlId, proof.CallId, kind, admission.BindingHash,
                root?.Id, proof.TargetDefinitionVersionId, root?.Revision, outcome, projection, WorkflowCollaborationCallPolicy.Hash(projection), unused);
            var json = JsonSerializer.Serialize(body, WorkflowJsonSerializerContext.Default.WorkflowCollaborationControlBody);
            var row = new WorkflowCollaborationControlRecord(controlId, proof.CallId, admission.SourceTenantScopeKey, admission.TargetTenantScopeKey,
                kind, json, WorkflowCollaborationCallPolicy.Hash(json), null, "published", 1, clock.UtcNow, null);
            if (!WorkflowCollaborationControlPolicy.TryRead(row, admission, out _)) return Invalid();
            if (await commands.ExecuteAsync(WorkflowCollaborationControlSql.Insert,
                    P(("Id", proof.CallId), ("ControlId", controlId), ("TenantScopeKey", scope.TenantScopeKey), ("BindingHash", admission.BindingHash),
                        ("TargetInstanceId", root?.Id), ("KindKey", kind), ("BodyJson", json), ("BodyHash", row.BodyHash), ("Now", row.CreatedAtUtc)), inner).ConfigureAwait(false) != 1) return Invalid();
            await outbox.AddAsync(WorkflowCollaborationControlPolicy.EventType(kind), 1,
                new WorkflowCollaborationControlIntegrationEvent(proof.CallId, controlId, admission.BindingHash, row.BodyHash),
                IntegrationEventMetadata.Create(proof.CallId.ToString("D"), "fullnet.workflow"), inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return saved.IsSuccess && saved.Value;
    }

    private async Task<string?> ProjectAsync(WorkflowInstanceRecord root, WorkflowCollaborationAcceptancePolicy accepted, WorkflowManagementScope scope,
        WorkflowPreparedTargetProjection? prepared, CancellationToken token)
    {
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            P(("DefinitionVersionId", root.DefinitionVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        var submission = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            P(("InstanceId", root.Id), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || submission is null || prepared is null || root.FormVersionId != asset.FormVersionId || prepared.InstanceId != root.Id ||
            prepared.SubmissionRevision != submission.Revision || prepared.SubmissionHash != WorkflowCollaborationCallPolicy.Hash(submission.SubmissionJson)) return null;
        try
        {
            var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            var values = JsonSerializer.Deserialize(submission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if (schema is null || values is null || !WorkflowFormValueValidator.Validate(schema, values)) return null;
            using var projected = JsonDocument.Parse(prepared.Json);
            if (!WorkflowVotePolicy.UniqueObject(projected.RootElement) || projected.RootElement.EnumerateObject().Any(property => !accepted.OutputFields.Values.Contains(property.Name, StringComparer.Ordinal))) return null;
            return prepared.Json;
        }
        catch (JsonException) { return null; }
    }
    private static Dictionary<string, object?> P(params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
