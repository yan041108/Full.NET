using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageDefinitions;

/// <summary>只读仿真复用发布准备、真实运行迁移及成员解析，不创建实例或调用任何副作用端口。</summary>
internal sealed class WorkflowSimulationService(WorkflowDefinitionManagementService definitions,
    WorkflowHumanActivityPreparation humans, IClock clock, WorkflowStarterSelectionQueryService selections)
{
    /// <summary>选择样例来自当前保存草稿，不借用最后发布版本或实例启动权限。</summary>
    public async Task<Result<WorkflowSimulationSelectionsResponse>> GetSelectionsAsync(Guid definitionId, long revision,
        Guid formVersionId, CancellationToken token)
    {
        var prepared = await definitions.PreparePublicationAsync(definitionId, new(revision, formVersionId), token).ConfigureAwait(false);
        if (!prepared.IsSuccess) return Result<WorkflowSimulationSelectionsResponse>.Failure(prepared.Error!);
        var input = prepared.Value!;
        var current = await VerifyRevisionAsync(definitionId, input.DraftRevision, token).ConfigureAwait(false);
        if (!current.IsSuccess) return Result<WorkflowSimulationSelectionsResponse>.Failure(current.Error!);
        return Result<WorkflowSimulationSelectionsResponse>.Success(new(definitionId, input.DraftRevision, input.FormVersion.Id,
            WorkflowStarterSelectionQueryService.Describe(input.Model)));
    }

    public async Task<Result<WorkflowRecipientCandidatePageResponse>> ListSelectionCandidatesAsync(Guid definitionId, long revision,
        Guid formVersionId, string selectionKey, int page, int pageSize, CancellationToken token)
    {
        var prepared = await definitions.PreparePublicationAsync(definitionId, new(revision, formVersionId), token).ConfigureAwait(false);
        if (!prepared.IsSuccess) return Result<WorkflowRecipientCandidatePageResponse>.Failure(prepared.Error!);
        var result = await selections.ListForModelAsync(prepared.Value!.Model, selectionKey, page, pageSize, token).ConfigureAwait(false);
        if (!result.IsSuccess) return result;
        // 目录读取期间可能发生草稿修订；迟到候选不能被显示成新草稿的选择范围。
        var current = await VerifyRevisionAsync(definitionId, prepared.Value.DraftRevision, token).ConfigureAwait(false);
        return current.IsSuccess ? result : Result<WorkflowRecipientCandidatePageResponse>.Failure(current.Error!);
    }

    private async Task<Result<WorkflowDefinitionResponse>> VerifyRevisionAsync(Guid definitionId, long revision, CancellationToken token)
    {
        var current = await definitions.GetAsync(definitionId, token).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        return current.Value!.DraftRevision == revision && current.Value.StatusKey == WorkflowDefinitionStatusKeys.Active
            ? current : Result<WorkflowDefinitionResponse>.Failure(new(WorkflowErrorCodes.RevisionConflict,
                "The workflow draft changed while preparing the simulation.", ErrorType.Conflict));
    }

    public async Task<Result<WorkflowSimulationResponse>> SimulateAsync(Guid definitionId, Guid actor,
        SimulateWorkflowDefinitionRequest request, CancellationToken token)
    {
        if (actor == Guid.Empty || request.Values.ValueKind != JsonValueKind.Object || request.Values.GetRawText().Length > 128 * 1024 ||
            request.Values.EnumerateObject().Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != request.Values.EnumerateObject().Count())
            return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        try
        {
            var preparation = await definitions.PreparePublicationAsync(definitionId, new(request.ExpectedRevision, request.FormVersionId), token).ConfigureAwait(false);
            if (!preparation.IsSuccess) return Result<WorkflowSimulationResponse>.Failure(preparation.Error!);
            var input = preparation.Value!;
            var values = request.Values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
            if (!TryEvaluate(input.Model, input.FormSchema, values, out var plan, out var transition) || plan is null)
                return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
            var selections = await humans.PrepareStartSelectionsAsync(input.Model, request.AssigneeSelections, input.Scope, token).ConfigureAwait(false);
            if (!selections.IsSuccess) return Result<WorkflowSimulationResponse>.Failure(selections.Error!);
            var members = await humans.PrepareAsync(transition, input.Scope, actor, token, values,
                starterSelections: selections.Value, flowAdministratorUserIds: plan.FlowAdministratorUserIds).ConfigureAwait(false);
            var trace = new List<WorkflowSimulationTraceResponse>();
            trace.Add(new(input.Model.Nodes.Single(node => node.NodeTypeKey == "start").NodeKey, "start", null, "sample_input", 0, null, null));
            Collect(transition, null);
            // 目录解析期间草稿可能被另一个编辑器更新；不能将旧轨迹显示成当前可发布草稿的结果。
            var current = await definitions.GetAsync(definitionId, token).ConfigureAwait(false);
            if (!current.IsSuccess) return Result<WorkflowSimulationResponse>.Failure(current.Error!);
            if (current.Value!.DraftRevision != input.DraftRevision || current.Value.StatusKey != WorkflowDefinitionStatusKeys.Active)
                return Fail(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
            return Result<WorkflowSimulationResponse>.Success(new(definitionId, input.DraftRevision, input.DraftContentHash,
                input.FormVersion.Id, input.FormVersion.ContentHash, input.Artifact.ContentHash,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.Values.GetRawText()))), clock.UtcNow,
                transition.CompletesInstance, trace));

            void Collect(WorkflowRuntimeTransition part, string? branch)
            {
                foreach (var node in part.AutomaticNodes)
                {
                    var count = 0; string? error = null;
                    if (node.NodeTypeKey == "notify.cc" && members.TryGetCc(node.NodeKey, out var cc))
                    { count = cc.IsSuccess ? cc.Value!.UserIds.Count : 0; error = cc.Error?.Code; }
                    trace.Add(new(node.NodeKey, node.NodeTypeKey, branch, node.OutcomeKey ?? "traversed", count, error, null));
                }
                if (part.NextHumanNodeKey is { } human)
                {
                    var count = 0; string? error = null; var outcome = "human_input_required";
                    if (members.TryGet(human, out var resolved))
                    {
                        error = resolved.Error?.Code;
                        if (resolved.IsSuccess)
                        {
                            var found = resolved.Value!;
                            var snapshot = found.ResolutionJson is null ? null : JsonSerializer.Deserialize(found.ResolutionJson, WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
                            count = snapshot?.MemberUserIds.Count ?? 0;
                            if (found.Handling is { } handling) outcome = "policy_" + handling.ActionKey;
                        }
                    }
                    else error = WorkflowErrorCodes.DefinitionAssigneePolicyInvalid;
                    trace.Add(new(human, plan.FindNodeTypeKey(human)!, branch, outcome, count, error, null));
                }
                if (part.AutomaticWait is { } wait)
                {
                    var node = input.Model.Nodes.Single(item => item.NodeKey == wait.NodeKey);
                    Guid? fixedVersion = node.NodeTypeKey == WorkflowSubprocessPolicy.NodeType && WorkflowSubprocessPolicy.TryRead(node.Config, input.FormSchema, out var child)
                        ? child.DefinitionVersionId : null;
                    trace.Add(new(wait.NodeKey, node.NodeTypeKey, branch, "runtime_boundary", 0, null, fixedVersion));
                }
                if (part.ParallelFork is { } fork)
                    foreach (var item in fork.Branches)
                        Collect(new(item.NextHumanNodeKey, item.CompletesInstance, item.AutomaticNodes, item.TimeoutPolicy,
                            item.ApprovalPolicy, item.AssigneePolicy, item.ParallelFork, item.JoinArrival, item.TaskPolicy, item.VotePolicy,
                            item.ClaimPolicy, item.AutomaticWait, item.TerminalNodeKey), branch is null ? item.BranchKey : branch + "/" + item.BranchKey);
                if (part.JoinArrival is { } join) trace.Add(new(join.JoinNodeKey, plan.FindNodeTypeKey(join.JoinNodeKey)!, branch, "join_boundary", 0, null, null));
                if (part.TerminalNodeKey is { } end) trace.Add(new(end, "end", branch, "end_reached", 0, null, null));
            }
        }
        catch (JsonException) { return Fail(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation); }
    }

    private static Result<WorkflowSimulationResponse> Fail(string code, ErrorType type) =>
        Result<WorkflowSimulationResponse>.Failure(new(code, "The workflow simulation could not be evaluated.", type));

    /// <summary>固定表单先校验样例类型，再交给实际运行计划；不做客户端类型转换或另一套条件求值。</summary>
    internal static bool TryEvaluate(WorkflowDefinitionDraft model, WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> values, out WorkflowRuntimePlan? plan, out WorkflowRuntimeTransition transition)
    {
        plan = null; transition = default;
        return WorkflowFormValueValidator.Validate(schema, values) && WorkflowRuntimePlan.TryCreate(model, schema, out plan) &&
            plan!.TryResolveStart(values, out transition);
    }
}

/// <summary>只引用服务器已保存草稿与已发布表单；发起人固定为当前可信操作人。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SimulateWorkflowDefinitionRequest(long ExpectedRevision, Guid FormVersionId, JsonElement Values,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>>? AssigneeSelections = null);
/// <summary>只返回固定输入摘要和求值轨迹，不回显样例业务值或将结果作为启动授权。</summary>
internal sealed record WorkflowSimulationResponse(Guid DefinitionId, long DraftRevision, string DraftContentHash,
    Guid FormVersionId, string FormContentHash, string CompiledContentHash, string SampleContentHash,
    DateTimeOffset EvaluatedAtUtc, bool ReachesEnd, IReadOnlyList<WorkflowSimulationTraceResponse> Trace);
/// <summary>轨迹解释真实命中与停止边界，成员仅返回当前权威解析后的数量。</summary>
internal sealed record WorkflowSimulationTraceResponse(string NodeKey, string NodeTypeKey, string? BranchKey,
    string OutcomeKey, int MemberCount, string? ErrorCode, Guid? FixedDefinitionVersionId);

/// <summary>仿真选人元数据绑定当前草稿与表单，不能作为发布版本或启动授权。</summary>
internal sealed record WorkflowSimulationSelectionsResponse(Guid DefinitionId, long DraftRevision, Guid FormVersionId,
    IReadOnlyList<WorkflowStarterSelectionDescriptorResponse> Items);
