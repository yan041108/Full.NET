using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution.Connectors;

/// <summary>异步只在原消费者屏障接纳已保存结果；准备或CAS竞争从不重新发送HTTP。</summary>
internal sealed partial class WorkflowConnectorProcessor
{
    internal async Task<bool> ApplyForBarrierAsync(WorkflowConnectorCallRecord call,WorkflowAsyncBarrierRecord barrier,CancellationToken token)
    {
        if(call.ModeKey!="async" || call.InstanceId!=barrier.InstanceId || call.TenantScopeKey!=barrier.TenantScopeKey ||
            call.ParallelJoinId!=barrier.ParallelJoinId || call.ParallelBranchKey!=barrier.ParallelBranchKey)return false;
        var instance=await FindInstanceAsync(call,token).ConfigureAwait(false);
        if(!Trusted(instance,call) || instance!.StatusKey!="active")return false;
        using var tenantScope=new WorkflowExecutionTenantScope(tenants,instance.TenantId);
        var owner=ids.NewId().ToString("N");
        var claimed=await transaction.ExecuteResultAsync(async inner=>{
            var current=await FindInstanceAsync(call,inner).ConfigureAwait(false);
            if(!Trusted(current,call) || current!.StatusKey!="active" || !await LockAsync(current,inner).ConfigureAwait(false) ||
                !await OwnsBarrierAsync(barrier,inner).ConfigureAwait(false))return Invalid<bool>();
            return await commands.ExecuteAsync(WorkflowConnectorSql.ClaimApply,Parameters(("Id",call.Id),("InstanceId",call.InstanceId),
                ("TenantScopeKey",call.TenantScopeKey),("Revision",call.Revision),("Owner",owner),("Expires",clock.UtcNow.AddSeconds(Math.Max(120,options.Value.LeaseSeconds))),
                ("Now",clock.UtcNow)),inner).ConfigureAwait(false)==1?Result<bool>.Success(true):Invalid<bool>();
        },token).ConfigureAwait(false);
        if(!claimed.IsSuccess)return false;
        var applying=call with{StatusKey="applying",LeaseOwnerKey=owner,LeaseGeneration=call.LeaseGeneration+1,ApplyAttemptCount=call.ApplyAttemptCount+1,Revision=call.Revision+1};
        if(applying.ApplyAttemptCount>Math.Min(8,options.Value.MaxAttempts))return await FaultApplyAsync(applying,"workflow.connector.apply_exhausted",token).ConfigureAwait(false);
        var runtime=await LoadRuntimeAsync(instance,applying,token).ConfigureAwait(false);
        var attempt=applying.SelectedAttemptId is { } attemptId?await query.QuerySingleOrDefaultAsync<WorkflowConnectorAttemptRecord>(WorkflowConnectorSql.FindAttempt,
            Parameters(("Id",attemptId),("CallId",applying.Id),("TenantScopeKey",applying.TenantScopeKey)),token).ConfigureAwait(false):null;
        if(runtime is null || attempt is not{StatusKey:"received",OutcomeKey:"succeeded",ResponseJson:{ } response} ||
            attempt.RequestHash!=applying.RequestHash || Hash(response)!=attempt.ResponseHash)
            return await FaultApplyAsync(applying,"workflow.connector.result_invalid",token).ConfigureAwait(false);
        var snapshot=await outputs.ReadSnapshotAsync(instance,applying.ParallelJoinId,applying.ParallelBranchKey,runtime.Schema,token).ConfigureAwait(false);
        if(snapshot is null || !WorkflowAsyncOutputPolicy.TargetsUnchanged(runtime.Policy,runtime.Values,snapshot.Values))
            return await FaultApplyAsync(applying,"workflow.connector.async_output_conflict",token).ConfigureAwait(false);
        if(!WorkflowAsyncOutputPolicy.TryMap(runtime.Policy,runtime.Values,snapshot.Values,response,out var output))
            return await FaultApplyAsync(applying,"workflow.connector.result_invalid",token).ConfigureAwait(false);
        var input=new WorkflowAutomaticInput(applying.Id,snapshot.Json,snapshot.Hash,snapshot.FormRevision,applying.ParallelJoinId,applying.ParallelBranchKey);
        var prepared=await outputs.PrepareAsync(instance,input,runtime.Schema,snapshot.Values,output!,runtime.Policy.CanRead,token).ConfigureAwait(false);
        if(prepared is null)
        {
            var latest=await outputs.ReadSnapshotAsync(instance,applying.ParallelJoinId,applying.ParallelBranchKey,runtime.Schema,token).ConfigureAwait(false);
            if(latest is not null && WorkflowAsyncOutputPolicy.TargetsUnchanged(runtime.Policy,runtime.Values,latest.Values) &&
                (latest.FormRevision!=snapshot.FormRevision || latest.Hash!=snapshot.Hash))
                return await YieldAsyncApplyAsync(applying,"workflow.connector.async_input_changed",token).ConfigureAwait(false);
            return await FaultApplyAsync(applying,"workflow.connector.input_or_attachment_invalid",token).ConfigureAwait(false);
        }
        try
        {
            var committed=await transaction.ExecuteResultAsync(async inner=>{
                var current=await FindInstanceAsync(applying,inner).ConfigureAwait(false);
                if(!Trusted(current,applying) || current!.StatusKey!="active" || !await LockAsync(current,inner).ConfigureAwait(false) ||
                    !await OwnsBarrierAsync(barrier,inner).ConfigureAwait(false) ||
                    !await outputs.CommitAsync(current,input,runtime.Schema,snapshot.Values,prepared,inner).ConfigureAwait(false))return Invalid<bool>();
                var now=clock.UtcNow;var revision=snapshot.FormRevision+(prepared.Output.Matched?1:0);
                if(await commands.ExecuteAsync(WorkflowSql.CompleteStepWithRevision,Parameters(("Id",applying.Id),("InstanceId",applying.InstanceId),
                    ("Revision",1L),("StatusKey","completed"),("CompletedAtUtc",now)),inner).ConfigureAwait(false)!=1)return Invalid<bool>();
                await commands.ExecuteAsync(WorkflowFieldActivitySql.InsertResult,Parameters(("Id",applying.Id),("InstanceId",applying.InstanceId),("TenantScopeKey",applying.TenantScopeKey),
                    ("InputFormRevision",snapshot.FormRevision),("OutputFormRevision",revision),("InputJson",snapshot.Json),("InputHash",snapshot.Hash),
                    ("OutputJson",prepared.OutputJson),("OutputHash",Hash(prepared.OutputJson)),("PatchJson",Serialize(prepared.Output.Patch)),
                    ("OutcomeKey",prepared.Output.Matched?"applied":"skipped"),("Now",now)),inner).ConfigureAwait(false);
                if(await commands.ExecuteAsync(WorkflowConnectorSql.FinishApply,Parameters(("Id",applying.Id),("InstanceId",applying.InstanceId),
                        ("TenantScopeKey",applying.TenantScopeKey),("Generation",applying.LeaseGeneration),("Owner",owner),("AttemptId",applying.SelectedAttemptId),
                        ("FormRevision",revision),("Now",now)),inner).ConfigureAwait(false)!=1 ||
                    await commands.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,Parameters(("Id",current.Id),("TenantScopeKey",current.TenantScopeKey),
                        ("Revision",current.Revision)),inner).ConfigureAwait(false)!=1)return Invalid<bool>();
                await AuditAsync(current,applying,"applied",applying.SelectedAttemptId,null,inner).ConfigureAwait(false);
                return Result<bool>.Success(true);
            },token).ConfigureAwait(false);
            if(!committed.IsSuccess)return await YieldAsyncApplyAsync(applying,"workflow.connector.async_output_retry",token).ConfigureAwait(false);
            return true;
        }
        finally{await outputs.FinishAsync(prepared,CancellationToken.None).ConfigureAwait(false);}
    }

    private async Task<bool> OwnsBarrierAsync(WorkflowAsyncBarrierRecord barrier,CancellationToken token)
    {
        var current=await query.QuerySingleOrDefaultAsync<WorkflowAsyncBarrierRecord>(WorkflowAsyncBarrierSql.Find,
            Parameters(("Id",barrier.Id),("InstanceId",barrier.InstanceId),("TenantScopeKey",barrier.TenantScopeKey)),token).ConfigureAwait(false);
        return current is{StatusKey:"applying"} && current.LeaseGeneration==barrier.LeaseGeneration && current.LeaseOwnerKey==barrier.LeaseOwnerKey && current.LeaseExpiresAtUtc>clock.UtcNow;
    }

    private async Task<bool> YieldAsyncApplyAsync(WorkflowConnectorCallRecord call,string error,CancellationToken token)
    {
        var yielded=await transaction.ExecuteResultAsync(async inner=>{
            var current=await FindInstanceAsync(call,inner).ConfigureAwait(false);
            if(!Trusted(current,call) || current!.StatusKey is "completed" or "rejected" or "cancelled" || !await LockAsync(current,inner).ConfigureAwait(false))return Invalid<bool>();
            if(await commands.ExecuteAsync(WorkflowConnectorSql.YieldApply,Parameters(("Id",call.Id),("InstanceId",call.InstanceId),("TenantScopeKey",call.TenantScopeKey),
                ("Generation",call.LeaseGeneration),("Owner",call.LeaseOwnerKey),("ErrorKey",error),("DueAtUtc",clock.UtcNow.AddSeconds(1)),("Now",clock.UtcNow)),inner).ConfigureAwait(false)!=1)return Invalid<bool>();
            await AuditAsync(current,call,"apply_retry",call.SelectedAttemptId,error,inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        },token).ConfigureAwait(false);
        return yielded.IsSuccess;
    }
}
