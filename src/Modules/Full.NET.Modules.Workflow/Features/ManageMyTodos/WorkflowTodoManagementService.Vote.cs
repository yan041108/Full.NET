using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>独立投票表单与主表单分开展示，历史只读本人本次Todo的私有视图。</summary>
internal sealed partial class WorkflowTodoManagementService
{
    private async Task<Result<WorkflowVoteDetail>> ReadVoteDetailAsync(WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, WorkflowRuntimeAssetRecord asset, WorkflowManagementScope scope,
        Guid actor, CancellationToken token)
    {
        var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var node = draft?.Nodes.SingleOrDefault(node => node.NodeKey == todo.NodeKey);
        if (node?.NodeTypeKey != WorkflowHumanActivityKinds.Vote || !WorkflowVotePolicy.TryRead(node.Config, out var policy)) return InvalidVoteDetail();
        var parameters = WorkflowSqlParameters.Create(("VoteId", todo.StepId), ("InstanceId", instance.Id),
            ("TodoId", todo.Id), ("ActorUserId", actor), ("TenantScopeKey", scope.TenantScopeKey),
            ("DefinitionVersionId", instance.DefinitionVersionId), ("NodeKey", todo.NodeKey));
        var round = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowVoteRoundRecord>(WorkflowVoteSql.FindRound, parameters, token).ConfigureAwait(false);
        var binding = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowVoteFormBindingRecord>(WorkflowVoteSql.FindFormBinding, parameters, token).ConfigureAwait(false);
        var personal = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoViewRecord>(WorkflowVoteSql.FindView, parameters, token).ConfigureAwait(false);
        if (round is null || binding is null || personal is null || round.Id != todo.StepId || round.InstanceId != instance.Id ||
            round.FormBindingId != binding.Id || binding.BallotFormVersionId != policy!.BallotFormVersionId ||
            binding.FormSchemaHash != HashUtf8(binding.FormSchemaJson) || round.MemberCount is < 1 or > 20 ||
            round.ParticipationPercentage != policy.ParticipationPercentage || round.ApprovalPercentage != policy.ApprovalPercentage ||
            round.AllowAbstain != policy.AllowAbstain || (round.ClosesAtUtc is null) != (policy.CloseAfterSeconds is null) ||
            round.StatusKey is not ("active" or "approved" or "rejected" or "suspended" or "cancelled")) return InvalidVoteDetail();
        var schema = JsonSerializer.Deserialize(binding.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        if (schema is null || !WorkflowVoteSubmissionRules.TryReadView(personal, schema, policy.AllowAbstain, out var view)) return InvalidVoteDetail();
        var counts = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowVoteCountsRecord>(databaseOptions.Value.Provider == DatabaseProvider.SqlServer
            ? WorkflowVoteSql.TallySqlServer : WorkflowVoteSql.TallyMySql, parameters, token).ConfigureAwait(false);
        if (counts is null || counts.YesCount is < 0 or > 20 || counts.NoCount is < 0 or > 20 || counts.AbstainCount is < 0 or > 20 ||
            counts.PendingCount is < 0 or > 20 || counts.CancelledCount is < 0 or > 20 ||
            counts.YesCount + counts.NoCount + counts.AbstainCount + counts.PendingCount + counts.CancelledCount != round.MemberCount)
            return InvalidVoteDetail();
        var closed = round.StatusKey != "active";
        if (closed && (round.FinalYesCount != counts.YesCount || round.FinalNoCount != counts.NoCount ||
            round.FinalAbstainCount != counts.AbstainCount || round.FinalUnsubmittedCount != counts.PendingCount + counts.CancelledCount) ||
            !closed && (round.FinalYesCount is not null || round.FinalNoCount is not null || round.FinalAbstainCount is not null ||
                round.FinalUnsubmittedCount is not null || counts.CancelledCount != 0)) return InvalidVoteDetail();
        var editable = todo.StatusKey == "active" && instance.StatusKey == "active" && round.StatusKey == "active" &&
            !view!.Submitted && (round.ClosesAtUtc is null || clock.UtcNow < round.ClosesAtUtc);
        var fields = schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey,
            field => editable ? field.Required ? "required" : "editable" : "readOnly", StringComparer.Ordinal);
        var version = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormVersionRecord>(WorkflowSql.FindFormVersionById,
            WorkflowSqlParameters.Create(("Id", binding.BallotFormVersionId), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false);
        if (version is null || HashUtf8(version.FormSchemaJson) != binding.FormSchemaHash) return InvalidVoteDetail();
        JsonElement? webRender = null;
        using (var json = JsonDocument.Parse(version.WebRenderSchemaJson))
        {
            if (json.RootElement.TryGetProperty("widgetList", out _))
            {
                var bindings = await WorkflowDesignerRelease.ReadBindingsAsync(queryExecutor, binding.BallotFormVersionId, scope, token).ConfigureAwait(false);
                if (!WorkflowDesignerRelease.Matches(bindings)) return InvalidVoteDetail();
                webRender = WorkflowDesignerAsset.CreateRuntimeView(version.WebRenderSchemaJson, fields);
                if (webRender is null) return InvalidVoteDetail();
                webRender = WorkflowDesignerRelease.AttachAuthorizedBindings(webRender.Value, bindings!, fields);
            }
        }
        var form = new WorkflowVoteFormResponse(binding.BallotFormVersionId, binding.FormSchemaHash,
            JsonSerializer.SerializeToElement(schema, WorkflowJsonSerializerContext.Default.WorkflowFormSchema), view!.Values,
            fields, view.Revision, view.ChoiceKey, !editable, policy.AllowAbstain, webRender);
        var progress = new WorkflowVoteProgressResponse(round.MemberCount, (round.MemberCount * policy.ParticipationPercentage + 99) / 100,
            (int)(counts.YesCount + counts.NoCount + counts.AbstainCount), (int)counts.PendingCount, (int)counts.CancelledCount,
            round.StatusKey, round.ClosesAtUtc, closed ? round.FinalYesCount : null, closed ? round.FinalNoCount : null,
            closed ? round.FinalAbstainCount : null, closed ? round.FinalUnsubmittedCount : null);
        return Result<WorkflowVoteDetail>.Success(new(form, progress));
    }

    private static Result<WorkflowVoteDetail> InvalidVoteDetail() => Result<WorkflowVoteDetail>.Failure(new(
        WorkflowErrorCodes.SchemaInvalid, "The private vote form or its frozen progress is unavailable.", ErrorType.BusinessRule));
}

/// <summary>内部详情组合不作为HTTP序列化契约。</summary>
internal sealed record WorkflowVoteDetail(WorkflowVoteFormResponse Form, WorkflowVoteProgressResponse Progress);
