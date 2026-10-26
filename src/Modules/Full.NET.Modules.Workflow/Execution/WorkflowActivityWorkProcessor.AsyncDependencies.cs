using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>只有服务器原屏障释放的固定工作项能跳过一次等待，跨实例/分支/修订证明一律失败。</summary>
internal sealed partial class WorkflowActivityWorkProcessor
{
    private async Task<bool> ValidateAsyncResumeAsync(WorkflowActivityWorkRecord work,CancellationToken token)
    {
        if(work.ResumeBarrierId is null)return true;
        if(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(work.InputJson)))!=work.InputHash)return false;
        var proof=await query.QuerySingleOrDefaultAsync<WorkflowAsyncBarrierRecord>(WorkflowAsyncBarrierSql.FindResume,
            WorkflowSqlParameters.Create(("BarrierId",work.ResumeBarrierId),("InstanceId",work.InstanceId),("TenantScopeKey",work.TenantScopeKey),
                ("WorkId",work.Id),("NodeKey",work.NodeKey),("InputHash",work.InputHash),("InputFormRevision",work.InputFormRevision)),token).ConfigureAwait(false);
        return MatchesResumeProof(work,proof);
    }

    internal static bool MatchesResumeProof(WorkflowActivityWorkRecord work,WorkflowAsyncBarrierRecord? proof)=>
        work.ResumeBarrierId is { } barrierId && barrierId!=Guid.Empty && work.Id!=Guid.Empty && work.InstanceId!=Guid.Empty && work.InputFormRevision>0 &&
        proof is not null && proof.Id==barrierId && proof.InstanceId==work.InstanceId &&
        proof.TenantScopeKey==work.TenantScopeKey && proof.NodeKey==work.NodeKey && proof.StatusKey=="released" && proof.ResumeWorkId==work.Id &&
        proof.OutputHash==work.InputHash && proof.OutputFormRevision==work.InputFormRevision && proof.ParallelJoinId==work.ParallelJoinId &&
        proof.ParallelBranchKey==work.ParallelBranchKey;

    private async Task<Result<bool>> FaultAsyncResumeAsync(WorkflowInstanceRecord instance,WorkflowActivityWorkRecord work,string owner,CancellationToken token)
    {
        if(!await FinishAsync(work,owner,"faulted","suspended",null,token).ConfigureAwait(false))return Invalid();
        if(await command.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision,WorkflowSqlParameters.Create(("Id",instance.Id),
                ("TenantScopeKey",instance.TenantScopeKey),("Revision",instance.Revision)),token).ConfigureAwait(false)!=1)return Invalid();
        await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,WorkflowSqlParameters.Create(("Id",ids.NewId()),("InstanceId",instance.Id),("StepId",null),
            ("CorrelationJson",Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),("TransitionKey","activity.async.invalid_resume"),
            ("FromStatusKey","active"),("ToStatusKey","suspended"),("IdempotencyKey",null),("Summary","workflow.activity.async_resume_invalid"),("CreatedAtUtc",clock.UtcNow)),token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }
}
