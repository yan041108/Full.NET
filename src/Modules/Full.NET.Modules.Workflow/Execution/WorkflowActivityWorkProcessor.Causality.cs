using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowActivityWorkProcessor
{
    /// <summary>只接受原工作保存的真实来源；跨代次只凭原路由或退回收据，不能默认回到根。</summary>
    private async Task<bool> ValidateSourceAsync(WorkflowActivityWorkRecord work,CancellationToken token)
    {
        if(work.SourceStepId is not {} source || source==Guid.Empty || !WorkflowActivationTrace.IsKnownKind(work.CauseKindKey))return false;
        if (work.ReturnRoundId is not null && (work.RouteDecisionId is not null || work.ResumeBarrierId is not null)) return false;
        return await query.QuerySingleOrDefaultAsync<int>(work.ReturnRoundId is not null ? WorkflowReturnRoundSql.CountWorkProof :
            work.RouteDecisionId is null?WorkflowCausalitySql.CountSourceFrame:WorkflowRouteSql.CountWorkProof,
            WorkflowSqlParameters.Create(("InstanceId",work.InstanceId),("TenantScopeKey",work.TenantScopeKey),("ExecutionEpochId",work.ExecutionEpochId),
                ("SourceStepId",source),("ParallelJoinId",work.ParallelJoinId),("ParallelBranchKey",work.ParallelBranchKey),("RouteDecisionId",work.RouteDecisionId),
                ("WorkId",work.Id),("NodeKey",work.NodeKey),("InputHash",work.InputHash),("CauseKindKey",work.CauseKindKey),
                ("ReturnRoundId",work.ReturnRoundId),("InputFormRevision",work.InputFormRevision)),token).ConfigureAwait(false)==1;
    }

    private async Task<Result<bool>> FaultSourceAsync(WorkflowInstanceRecord instance,WorkflowActivityWorkRecord work,string owner,CancellationToken token,
        string transitionKey = "activity.causality.invalid_source", string errorCode = "workflow.causality.source_missing")
    {
        if (!await FinishAsync(work,owner,"faulted","suspended",null,token).ConfigureAwait(false)) return Invalid();
        if (await command.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision,WorkflowSqlParameters.Create(("Id",instance.Id),
                ("TenantScopeKey",instance.TenantScopeKey),("Revision",instance.Revision)),token).ConfigureAwait(false)!=1) return Invalid();
        await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,WorkflowSqlParameters.Create(("Id",ids.NewId()),("InstanceId",instance.Id),("StepId",null),
            ("CorrelationJson",Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),("TransitionKey",transitionKey),
            ("FromStatusKey","active"),("ToStatusKey","suspended"),("IdempotencyKey",null),("Summary",errorCode),("CreatedAtUtc",clock.UtcNow)),token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }
}
