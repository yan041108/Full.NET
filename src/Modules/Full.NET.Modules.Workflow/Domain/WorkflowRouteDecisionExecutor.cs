using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>调用者持有真实根锁并使用同一事务；选择、区域换代、后继Work及应用收据共同提交。</summary>
internal sealed class WorkflowRouteDecisionExecutor(IQueryExecutor query,ICommandExecutor command,IIdGenerator ids,
    WorkflowActivityScheduler scheduler,WorkflowBranchStateCoordinator branches,WorkflowRegionEpochCoordinator regions)
{
    public async Task<WorkflowRouteApplication> ActivateAsync(WorkflowInstanceRecord instance,WorkflowActivityWorkRecord work,
        WorkflowDynamicRoutePlan plan,JsonElement configuration,IReadOnlyDictionary<string,JsonElement> values,
        WorkflowActivationTrace trace,long sequence,DateTimeOffset now,CancellationToken token,bool hasSubprocess=false)
    {
        if(instance.StatusKey!="active" || work.InstanceId!=instance.Id || work.TenantScopeKey!=instance.TenantScopeKey ||
            !plan.Routes.TryGetValue(work.NodeKey,out var route) ||
            !await WorkflowExecutionFrameScope.IsValidAsync(query,instance,work.ExecutionEpochId,work.ParallelJoinId,work.ParallelBranchKey,token).ConfigureAwait(false))return Invalid();
        var policyHash=WorkflowJsonCanonicalizer.Compile(configuration).ContentHash;
        var parameters=WorkflowSqlParameters.Create(("Id",work.Id),("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey));
        var existing=await query.QuerySingleOrDefaultAsync<WorkflowRouteDecisionRecord>(WorkflowRouteSql.Find,parameters,token).ConfigureAwait(false);
        if(existing is not null)
        {
            if(!Matches(instance,work,existing,policyHash))return Invalid();
            if(existing.StatusKey=="decision_unknown")return new(false,true,null);
            // 已应用工作不可能合法重新领取；不能由一次重试建立第二个新代次。
            return Invalid();
        }
        var used=await query.QuerySingleOrDefaultAsync<int>(WorkflowActivitySql.CountActivations,
            WorkflowSqlParameters.Create(("InstanceId",instance.Id)),token).ConfigureAwait(false);
        if(await WorkflowExecutionBudgetPolicy.ReadRemainingAsync(instance,hasSubprocess,used,query,token).ConfigureAwait(false)<1)
            return new(false,false,"workflow.activity.budget_exhausted");
        var selected=route.Policy.TrySelect(values,out var target);
        var next=selected?(target is null?route.NormalNextNodeKey:target.NodeKey):null;
        Guid? nextWork=selected?ids.NewId():null;
        var input=WorkflowActivityScheduler.SerializeInput(values);
        if(Hash(input)!=work.InputHash || input!=work.InputJson)return Invalid();
        var write=WorkflowSqlParameters.Create(("Id",work.Id),("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey),
            ("ExecutionEpochId",work.ExecutionEpochId),("DefinitionVersionId",instance.DefinitionVersionId),("NodeKey",work.NodeKey),
            ("StepStatusKey",selected?"completed":"active"),("ExecutionSequence",sequence),("ParallelJoinId",work.ParallelJoinId),
            ("ParallelBranchKey",work.ParallelBranchKey),("InputJson",input),("InputHash",work.InputHash),("InputFormRevision",work.InputFormRevision),
            ("PolicyHash",policyHash),("StatusKey",selected?"ready":"decision_unknown"),("SelectedTargetKey",target?.TargetKey),
            ("NextNodeKey",next),("NextWorkId",nextWork),("Now",now));
        if(await command.ExecuteAsync(WorkflowRouteSql.InsertStep,write,token).ConfigureAwait(false)!=1)
            throw new InvalidOperationException("workflow.route.step_conflict");
        await WorkflowActivationBindingWriter.BindAsync(command,instance.Id,instance.TenantScopeKey,work.ExecutionEpochId,work.Id,now,token,trace,ids).ConfigureAwait(false);
        if(await command.ExecuteAsync(WorkflowRouteSql.Insert,write,token).ConfigureAwait(false)!=1)
            throw new InvalidOperationException("workflow.route.decision_conflict");
        var detail=WorkflowJsonCanonicalizer.Compile(writer=>{
            writer.WriteStartObject(); writer.WriteString("nodeTypeKey",WorkflowDynamicRoutePolicy.NodeType);
            writer.WriteString("inputHash",work.InputHash); writer.WriteString("policyHash",policyHash);
            writer.WriteString("statusKey",selected?"ready":"decision_unknown"); writer.WriteString("selectedTargetKey",target?.TargetKey); writer.WriteEndObject();
        }).CanonicalJson;
        await command.ExecuteAsync(WorkflowSql.InsertDomainAudit,WorkflowSqlParameters.Create(("Id",ids.NewId()),("TenantId",instance.TenantId),
            ("ScopeKey",instance.ScopeKey),("InstanceId",instance.Id),("OperationKey","activity.execute"),("ActorUserId",null),
            ("ResourceTypeKey","step"),("ResourceId",work.Id),("OutcomeKey","succeeded"),("DetailJson",detail),("CreatedAtUtc",now),
            ("CorrelationJson",Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson())),token).ConfigureAwait(false);
        var frozen=await query.QuerySingleOrDefaultAsync<WorkflowRouteDecisionRecord>(WorkflowRouteSql.Find,parameters,token).ConfigureAwait(false);
        if(frozen is null || !Matches(instance,work,frozen,policyHash))throw new InvalidOperationException("workflow.route.decision_missing");
        if(!selected)return new(false,true,null);
        var applied=await ApplyReadyAsync(instance,frozen,plan,work.Id,now,token).ConfigureAwait(false);
        if(applied.FailureKey is not ("workflow.activity.region_iteration_exhausted" or "workflow.activity.region_depth_exhausted" or "workflow.activity.region_unsettled"))return applied;
        // 区域协调器的拒绝发生于任何换代写入之前；保留原输入并挂起，不能反复重领直到越过预算。
        var hold=WorkflowSqlParameters.Create(("Id",frozen.Id),("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey),("Revision",frozen.Revision));
        if(await command.ExecuteAsync(WorkflowRouteSql.HoldSelection,hold,token).ConfigureAwait(false)!=1 ||
            await command.ExecuteAsync(WorkflowRouteSql.HoldStep,hold,token).ConfigureAwait(false)!=1)
            throw new InvalidOperationException("workflow.route.hold_conflict");
        return new(false,true,applied.FailureKey);
    }

    /// <summary>只消费已经固化的选择；人工修复与首次执行使用完全相同的区域及唯一后继接纳逻辑。</summary>
    public async Task<WorkflowRouteApplication> ApplyReadyAsync(WorkflowInstanceRecord instance,WorkflowRouteDecisionRecord frozen,
        WorkflowDynamicRoutePlan plan,Guid? currentWorkId,DateTimeOffset now,CancellationToken token)
    {
        if(instance.StatusKey!="active" || frozen.InstanceId!=instance.Id || frozen.TenantScopeKey!=instance.TenantScopeKey ||
            frozen.DefinitionVersionId!=instance.DefinitionVersionId || frozen.StatusKey!="ready" || frozen.TargetEpochId is not null ||
            frozen.NextWorkId is not {} workId || workId==Guid.Empty || !plan.Routes.TryGetValue(frozen.NodeKey,out var route))return Invalid();
        var selected=frozen.SelectedTargetKey is null?null:route.Targets.SingleOrDefault(target=>target.Target.TargetKey==frozen.SelectedTargetKey);
        if(frozen.SelectedTargetKey is not null && selected is null || frozen.NextNodeKey!=(selected?.Target.NodeKey??route.NormalNextNodeKey))return Invalid();
        var values=JsonSerializer.Deserialize(frozen.InputJson,WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        if(values is null || Hash(frozen.InputJson)!=frozen.InputHash)return Invalid();
        var change=selected?.Reenters==true
            ? await regions.EnterAsync(instance,frozen.ExecutionEpochId,frozen.Id,plan,selected,currentWorkId,now,token).ConfigureAwait(false)
            : await regions.ExitAsync(instance,frozen.ExecutionEpochId,frozen.Id,plan,frozen.NextNodeKey!,currentWorkId,now,token).ConfigureAwait(false);
        if(!change.IsSuccess)return new(false,false,change.FailureKey);
        var epoch=change.ExecutionEpochId!.Value;
        // 结构化路由只能留在同一真实分支，区域退出恢复父代次但不能借机跳入兄弟分支。
        if(!await WorkflowExecutionFrameScope.IsValidAsync(query,instance,epoch,frozen.ParallelJoinId,frozen.ParallelBranchKey,token).ConfigureAwait(false))
            throw new InvalidOperationException("workflow.route.target_frame_conflict");
        var revision=await branches.ReadRevisionAsync(instance,frozen.ParallelJoinId,frozen.ParallelBranchKey,token).ConfigureAwait(false);
        if(revision!=frozen.InputFormRevision)throw new InvalidOperationException("workflow.route.input_revision_conflict");
        var bind=WorkflowSqlParameters.Create(("Id",frozen.Id),("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey),
            ("Revision",frozen.Revision),("TargetEpochId",epoch),("NextWorkId",workId),("TargetParallelJoinId",frozen.ParallelJoinId),
            ("TargetParallelBranchKey",frozen.ParallelBranchKey),("NextNodeKey",frozen.NextNodeKey),("Now",now));
        if(await command.ExecuteAsync(WorkflowRouteSql.BindContinuation,bind,token).ConfigureAwait(false)!=1)
            throw new InvalidOperationException("workflow.route.continuation_conflict");
        var sequence=await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
            WorkflowSqlParameters.Create(("InstanceId",instance.Id)),token).ConfigureAwait(false);
        await scheduler.EnqueueAsync(instance,epoch,frozen.NextNodeKey!,values,revision!.Value,sequence,frozen.ParallelJoinId,frozen.ParallelBranchKey,
            now,token,workId:workId,sourceStepId:frozen.Id,causeKindKey:"region",routeDecisionId:frozen.Id).ConfigureAwait(false);
        var applied=WorkflowSqlParameters.Create(("Id",frozen.Id),("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey),
            ("Revision",frozen.Revision+1),("TargetEpochId",epoch),("NextWorkId",workId),("NextNodeKey",frozen.NextNodeKey),("Now",now));
        if(await command.ExecuteAsync(WorkflowRouteSql.MarkApplied,applied,token).ConfigureAwait(false)!=1)
            throw new InvalidOperationException("workflow.route.apply_conflict");
        return new(true,false,null);
    }

    private static bool Matches(WorkflowInstanceRecord instance,WorkflowActivityWorkRecord work,WorkflowRouteDecisionRecord row,string hash)=>
        row.Id==work.Id && row.InstanceId==instance.Id && row.TenantScopeKey==instance.TenantScopeKey && row.ExecutionEpochId==work.ExecutionEpochId &&
        row.DefinitionVersionId==instance.DefinitionVersionId && row.NodeKey==work.NodeKey && row.InputJson==work.InputJson && row.InputHash==work.InputHash &&
        row.InputFormRevision==work.InputFormRevision && row.PolicyHash==hash && row.ParallelJoinId==work.ParallelJoinId && row.ParallelBranchKey==work.ParallelBranchKey;
    internal static string Hash(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static WorkflowRouteApplication Invalid()=>new(false,false,"workflow.route.invalid_transition");
}

internal sealed record WorkflowRouteApplication(bool Applied,bool DecisionUnknown,string? FailureKey);
