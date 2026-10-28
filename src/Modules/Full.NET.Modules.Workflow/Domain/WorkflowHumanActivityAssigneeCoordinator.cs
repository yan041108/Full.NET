using Full.NET.Abstractions.Results;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在节点激活前把固化办理人策略与多人审批策略收敛为可写入步骤的快照。</summary>
/// <param name="resolver">办理人解析器。</param>
internal sealed class WorkflowHumanActivityAssigneeCoordinator(WorkflowAssigneeResolver resolver)
{
    /// <summary>原目标的成员顺序和组边界保持不变；仅批量复核现在仍是可办理的活动身份。</summary>
    public async Task<Result<WorkflowHumanActivityAssignees>> RestoreReturnAsync(string nodeType, System.Text.Json.JsonElement configuration,
        string? json, string? hash, WorkflowManagementScope scope, Guid initiator, CancellationToken token)
    {
        if (!WorkflowReturnAssigneeSnapshot.TryRestore(nodeType, configuration, initiator, json, hash, out var restored)) return Failure();
        var original = JsonSerializer.Deserialize(json!, WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot)!;
        var sources = original.Sources.Concat(original.Groups?.SelectMany(group => group.Resolution.Sources) ?? []);
        var containsInitiator = sources.Any(source => source?.Config?.ResolverKindKey == WorkflowAssigneePolicy.Initiator &&
            source.UserIds is not null && source.UserIds.Contains(initiator));
        var members = restored!.Task?.MemberUserIds ?? restored.ApprovalPolicy?.ApproverUserIds ?? [restored.FallbackAssigneeUserId];
        return await resolver.ValidateResolvedAsync(members, scope, initiator, true, containsInitiator, token).ConfigureAwait(false)
            ? Result<WorkflowHumanActivityAssignees>.Success(restored) : Failure();
    }

    /// <summary>抄送只解析收件人：正常无人可以留空，失效身份与来源不能被忽略或替换成发起人。</summary>
    public async Task<Result<WorkflowCcRecipients>> ResolveCcAsync(WorkflowAssigneePolicy policy, WorkflowManagementScope scope,
        Guid initiator, WorkflowAssigneeRuntimeContext context, CancellationToken token)
    {
        if (policy.Handling is not null) return Result<WorkflowCcRecipients>.Failure(new(WorkflowErrorCodes.DefinitionCcRecipientsInvalid,
            "Recipient policies cannot contain human handling actions.", ErrorType.BusinessRule));
        var capture = new WorkflowAssigneeResolutionCapture();
        var resolved = await resolver.ResolveAsync(policy, [], scope, initiator, token, capture: capture, context: context, allowEmpty: true).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<WorkflowCcRecipients>.Failure(resolved.Error!);
        var snapshot = capture.Create(initiator, resolved.Value!);
        snapshot = snapshot with { Sources = snapshot.Sources.Select(source => source with { OriginKey = "recipient_policy" }).ToArray() };
        var json = JsonSerializer.Serialize(snapshot, WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 128 * 1024) return Result<WorkflowCcRecipients>.Failure(new(WorkflowErrorCodes.DefinitionCcRecipientsInvalid,
            "The recipient snapshot exceeds the supported limit.", ErrorType.BusinessRule));
        return Result<WorkflowCcRecipients>.Success(new(resolved.Value!, json, Convert.ToHexStringLower(SHA256.HashData(bytes))));
    }

    /// <summary>解析下一审批等待点的有效办理人集合。</summary>
    /// <param name="assigneePolicy">发布版本固化的办理人策略。</param>
    /// <param name="approvalPolicy">发布版本固化的多人审批策略；为空时要求解析出唯一办理人。</param>
    /// <param name="scope">当前可信工作流管理作用域。</param>
    /// <param name="initiatorUserId">流程发起人标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>多人策略或单人 fallback 办理人；解析失败时返回业务错误。</returns>
    public async Task<Result<WorkflowHumanActivityAssignees>> ResolveAsync(
        WorkflowAssigneePolicy assigneePolicy,
        WorkflowApprovalPolicy? approvalPolicy,
        WorkflowManagementScope scope,
        Guid initiatorUserId,
        CancellationToken cancellationToken = default,
        WorkflowAssigneeRuntimeContext? context = null)
    {
        if (approvalPolicy?.GroupConfiguration is { } groups)
            return await ResolveGroupsAsync(groups, scope, initiatorUserId, cancellationToken, context).ConfigureAwait(false);
        var explicitApproverUserIds = approvalPolicy?.ApproverUserIds ?? [];
        var capture = new WorkflowAssigneeResolutionCapture();
        var resolved = await WorkflowAssigneeHandlingEvaluator.EvaluateAsync(resolver,
                assigneePolicy,
                explicitApproverUserIds,
                scope,
                initiatorUserId,
                capture, cancellationToken, context)
            .ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Result<WorkflowHumanActivityAssignees>.Failure(resolved.Error!);
        }

        var handled = resolved.Value!;
        var userIds = handled.Members;
        var resolutionJson = JsonSerializer.Serialize(capture.Create(initiatorUserId, userIds, handled.Disposition),
            WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        var bytes = Encoding.UTF8.GetBytes(resolutionJson);
        if (bytes.Length > 128*1024) return Failure();
        var resolutionHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (handled.Disposition is { } disposition)
            return Result<WorkflowHumanActivityAssignees>.Success(new(null, Guid.Empty, resolutionJson, resolutionHash, Handling: disposition));
        if (approvalPolicy is not null)
        {
            if (userIds.Count is < 1 or > 20)
            {
                return Failure();
            }
            // 解析来源可能扩充或去重显式成员，门槛必须按本次实际席位冻结，不能沿用配置列表的旧分母。
            var required = approvalPolicy.ResolveRequiredCount(userIds.Count);
            if (required == 0) return Failure();

            return Result<WorkflowHumanActivityAssignees>.Success(
                new WorkflowHumanActivityAssignees(
                    new WorkflowApprovalPolicy(
                        approvalPolicy.ModeKey,
                        userIds,
                        required, approvalPolicy.RequiredPercentage),
                    Guid.Empty, resolutionJson, resolutionHash));
        }

        if (userIds.Count != 1)
        {
            return Failure();
        }

        return Result<WorkflowHumanActivityAssignees>.Success(
            new WorkflowHumanActivityAssignees(null, userIds[0], resolutionJson, resolutionHash));
    }

    /// <summary>办理只冻结实际提交成员，不构造审批策略或同意票门槛。</summary>
    public async Task<Result<WorkflowHumanActivityAssignees>> ResolveTaskAsync(WorkflowAssigneePolicy assignees,
        WorkflowTaskPolicy policy, WorkflowManagementScope scope, Guid initiator, CancellationToken token, WorkflowAssigneeRuntimeContext? context = null)
    {
        var capture = new WorkflowAssigneeResolutionCapture();
        var members = await WorkflowAssigneeHandlingEvaluator.EvaluateAsync(resolver, assignees, [], scope, initiator,
            capture, token, context).ConfigureAwait(false);
        if (!members.IsSuccess) return Result<WorkflowHumanActivityAssignees>.Failure(members.Error!);
        if (members.Value!.Disposition is not null) return Handled(members.Value, capture, initiator);
        var userIds = members.Value.Members;
        if (policy.RequiredCompletions(userIds.Count) == 0) return Failure();
        var json = JsonSerializer.Serialize(capture.Create(initiator, userIds),
            WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 128 * 1024) return Failure();
        return Result<WorkflowHumanActivityAssignees>.Success(new(null, Guid.Empty, json,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), new(policy, userIds)));
    }

    /// <summary>投票固定实际候选分母和来源依据，不构造审批席位或同意票。</summary>
    public async Task<Result<WorkflowHumanActivityAssignees>> ResolveVoteAsync(WorkflowAssigneePolicy assignees,
        WorkflowVotePolicy policy, WorkflowManagementScope scope, Guid initiator, CancellationToken token, WorkflowAssigneeRuntimeContext? context = null)
    {
        var capture = new WorkflowAssigneeResolutionCapture();
        var members = await WorkflowAssigneeHandlingEvaluator.EvaluateAsync(resolver, assignees, [], scope, initiator,
            capture, token, context).ConfigureAwait(false);
        if (!members.IsSuccess) return Result<WorkflowHumanActivityAssignees>.Failure(members.Error!);
        if (members.Value!.Disposition is { } disposition)
        {
            // 没有任何可投成员时不能把自动通过或本人跳过解释为同意票；与零有效票保持同一挂起边界。
            var handled = disposition.ActionKey is "auto_pass" or "skip"
                ? members.Value with { Disposition = new("suspend", "no_valid_vote") } : members.Value;
            return Handled(handled, capture, initiator);
        }
        var users = members.Value.Members;
        if (users.Count is < 1 or > 20) return Failure();
        var json = JsonSerializer.Serialize(capture.Create(initiator, users), WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 128 * 1024) return Failure();
        return Result<WorkflowHumanActivityAssignees>.Success(new(null, Guid.Empty, json,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), Vote: new(policy, users)));
    }

    /// <summary>冻结候选池的原始来源与候选，不把候选转换为个人待办。</summary>
    public async Task<Result<WorkflowHumanActivityAssignees>> ResolveClaimAsync(WorkflowAssigneePolicy assignees,
        WorkflowClaimPolicy policy, WorkflowManagementScope scope, Guid initiator, CancellationToken token, WorkflowAssigneeRuntimeContext? context = null)
    {
        var capture = new WorkflowAssigneeResolutionCapture();
        var members = await WorkflowAssigneeHandlingEvaluator.EvaluateAsync(resolver, assignees, [], scope, initiator,
            capture, token, context).ConfigureAwait(false);
        if (!members.IsSuccess) return Result<WorkflowHumanActivityAssignees>.Failure(members.Error!);
        if (members.Value!.Disposition is not null) return Handled(members.Value, capture, initiator);
        var users = members.Value.Members;
        if (users.Count is < 1 or > WorkflowClaimPolicy.MaximumCandidates) return Failure();
        var json = JsonSerializer.Serialize(capture.Create(initiator, users), WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 128 * 1024) return Failure();
        return Result<WorkflowHumanActivityAssignees>.Success(new(null, Guid.Empty, json,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), Claim: new(policy, users)));
    }

    /// <summary>各组先收集来源，拒绝交叉占席后一次校验最终活动成员，并保存组与来源的对应依据。</summary>
    private async Task<Result<WorkflowHumanActivityAssignees>> ResolveGroupsAsync(WorkflowGroupApprovalConfiguration config,
        WorkflowManagementScope scope, Guid initiator, CancellationToken token, WorkflowAssigneeRuntimeContext? context)
    {
        var users = new List<Guid>(); var seen = new HashSet<Guid>();
        var groups = new List<WorkflowApprovalGroupAssignees>(); var snapshots = new List<WorkflowAssigneeGroupResolution>();
        var observed = new HashSet<Guid>(); var hasInitiator = false;
        foreach (var group in config.Groups)
        {
            var capture = new WorkflowAssigneeResolutionCapture();
            var collected = await WorkflowAssigneeHandlingEvaluator.EvaluateAsync(resolver, group.AssigneePolicy, group.ApprovalPolicy?.ApproverUserIds ?? [],
                scope, initiator, capture, token, context, validateIdentities: false).ConfigureAwait(false);
            if (!collected.IsSuccess) return Result<WorkflowHumanActivityAssignees>.Failure(collected.Error!);
            var handled = collected.Value!; observed.UnionWith(handled.ObservedMembers); hasInitiator |= handled.ContainsInitiatorSource;
            var members = handled.Disposition is null ? handled.Members : [];
            if (members.Any(user => !seen.Add(user))) return Failure();
            users.AddRange(members);
            if (users.Count > 20) return Failure();
            var required = handled.Disposition is null ? group.ApprovalPolicy?.ResolveRequiredCount(members.Count) ?? (members.Count == 1 ? 1 : 0) : 0;
            if (required == 0 && handled.Disposition is null) return Failure();
            groups.Add(new(group.GroupKey, group.GroupName,
                new(handled.Disposition is null ? group.ApprovalPolicy?.ModeKey ?? "single" : "system", members, required,
                    group.ApprovalPolicy?.RequiredPercentage), handled.Disposition));
            snapshots.Add(new(group.GroupKey, group.GroupName, capture.Create(initiator, members, handled.Disposition)));
        }
        if (!await resolver.ValidateObservedAsync(observed.ToArray(), scope, initiator, true, hasInitiator, token).ConfigureAwait(false)) return Failure();
        var disposition = groups.Select(group => group.Handling).FirstOrDefault(handling => handling?.ActionKey == "auto_reject")
            ?? groups.Select(group => group.Handling).FirstOrDefault(handling => handling?.ActionKey == "suspend");
        if (disposition is null && groups.Count(group => group.Handling?.ActionKey is "auto_pass" or "skip") >= config.RequiredGroups)
            disposition = new("auto_pass", "group_threshold");
        var json = JsonSerializer.Serialize(new WorkflowAssigneeResolutionSnapshot(2, initiator, users, [], snapshots, disposition),
            WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 128 * 1024) return Failure();
        return Result<WorkflowHumanActivityAssignees>.Success(new(
            new("group", users, config.RequiredGroups, GroupConfiguration: config, Groups: groups), Guid.Empty,
            json, Convert.ToHexStringLower(SHA256.HashData(bytes)), Handling: disposition));
    }

    /// <summary>系统处置只冻结成员依据，不构造人工成员、门槛或虚假个人待办。</summary>
    private static Result<WorkflowHumanActivityAssignees> Handled(WorkflowHandledAssignees handled,
        WorkflowAssigneeResolutionCapture capture, Guid initiator)
    {
        var json = JsonSerializer.Serialize(capture.Create(initiator, handled.Members, handled.Disposition),
            WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        var bytes = Encoding.UTF8.GetBytes(json);
        return bytes.Length > 128 * 1024 ? Failure() : Result<WorkflowHumanActivityAssignees>.Success(
            new(null, Guid.Empty, json, Convert.ToHexStringLower(SHA256.HashData(bytes)), Handling: handled.Disposition));
    }

    /// <summary>构造办理人解析失败的统一业务错误。</summary>
    /// <returns>稳定验证错误。</returns>
    private static Result<WorkflowHumanActivityAssignees> Failure() =>
        Result<WorkflowHumanActivityAssignees>.Failure(new Error(
            WorkflowErrorCodes.DefinitionAssigneePolicyInvalid,
            "The assignee policy could not be resolved.",
            ErrorType.Validation));
}

/// <summary>描述审批节点激活时写入步骤的办理人快照。</summary>
/// <param name="ApprovalPolicy">多人审批策略；为空时表示单人办理。</param>
/// <param name="FallbackAssigneeUserId">单人办理时的办理人标识。</param>
internal sealed record WorkflowHumanActivityAssignees(
    WorkflowApprovalPolicy? ApprovalPolicy,
    Guid FallbackAssigneeUserId,
    string? ResolutionJson = null,
    string? ResolutionHash = null,
    WorkflowTaskActivationPolicy? Task = null,
    WorkflowVoteActivationPolicy? Vote = null,
    WorkflowClaimActivationPolicy? Claim = null,
    WorkflowAssigneeHandlingDisposition? Handling = null);
