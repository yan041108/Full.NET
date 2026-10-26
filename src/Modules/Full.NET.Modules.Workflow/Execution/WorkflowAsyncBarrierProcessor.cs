using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Observability;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution.Connectors;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>消费者按原依赖耐久等待，逐个安全接纳，最后以独立租约一次释放受信续办。</summary>
internal sealed class WorkflowAsyncBarrierProcessor(IQueryExecutor query,ICommandExecutor commands,ICommandTransaction transaction,
    IClock clock,IIdGenerator ids,ICurrentTenantContextWriter tenants,IOptions<DatabaseOptions> database,IOptions<WorkflowRecoveryWorkerOptions> options,
    WorkflowAutomaticOutputCoordinator outputs,WorkflowActivityScheduler scheduler,WorkflowConnectorProcessor connectors)
{
    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        var statement=database.Value.Provider==DatabaseProvider.SqlServer?WorkflowAsyncBarrierSql.ListDueSqlServer:WorkflowAsyncBarrierSql.ListDueMySql;
        var rows=await query.QueryAsync<WorkflowAsyncBarrierRecord>(statement,Parameters(("Now",clock.UtcNow),("Take",Math.Min(4,options.Value.BatchSize))),token).ConfigureAwait(false);
        var progressed=0;
        foreach(var candidate in rows)
        {
            token.ThrowIfCancellationRequested();
            var instance=await InstanceAsync(candidate,token).ConfigureAwait(false);
            if(!Trusted(instance,candidate) || instance!.StatusKey!="active")continue;
            using var tenantScope=new WorkflowExecutionTenantScope(tenants,instance.TenantId);
            var owner=ids.NewId().ToString("N");var expires=clock.UtcNow.AddSeconds(Math.Max(300,options.Value.LeaseSeconds));
            var claimed=await transaction.ExecuteResultAsync(async inner=>{
                var current=await InstanceAsync(candidate,inner).ConfigureAwait(false);
                if(!Trusted(current,candidate) || current!.StatusKey!="active" || !await LockAsync(current,inner).ConfigureAwait(false))return Invalid<bool>();
                return await commands.ExecuteAsync(WorkflowAsyncBarrierSql.Claim,Parameters(("Id",candidate.Id),("InstanceId",candidate.InstanceId),
                    ("TenantScopeKey",candidate.TenantScopeKey),("Revision",candidate.Revision),("Owner",owner),("Expires",expires),("Now",clock.UtcNow)),inner).ConfigureAwait(false)==1
                    ?Result<bool>.Success(true):Invalid<bool>();
            },token).ConfigureAwait(false);
            if(!claimed.IsSuccess)continue;
            var barrier=candidate with{StatusKey="applying",LeaseOwnerKey=owner,LeaseExpiresAtUtc=expires,LeaseGeneration=candidate.LeaseGeneration+1,Revision=candidate.Revision+1};
            try{if(await ProcessAsync(instance,barrier,token).ConfigureAwait(false))progressed++;}
            catch(DbException){await YieldAsync(barrier,barrier.FailureCount+1,"workflow.activity.async_storage_retry",hardFailure:false,suspend:false,token).ConfigureAwait(false);}
        }
        return progressed;
    }

    private async Task<bool> ProcessAsync(WorkflowInstanceRecord instance,WorkflowAsyncBarrierRecord barrier,CancellationToken token)
    {
        var runtime=await LoadRuntimeAsync(instance,barrier,token).ConfigureAwait(false);
        if(runtime is null){await YieldAsync(barrier,barrier.FailureCount,"workflow.activity.async_dependency_invalid",true,true,token).ConfigureAwait(false);return true;}
        var calls=await CallsAsync(barrier,runtime.Dependencies,token).ConfigureAwait(false);
        if(!ValidCalls(barrier,runtime,calls)){await YieldAsync(barrier,barrier.FailureCount,"workflow.activity.async_dependency_invalid",true,true,token).ConfigureAwait(false);return true;}
        if(calls.Any(call=>call.StatusKey is "outcome_unknown" or "failed" or "rejected"))
        {await YieldAsync(barrier,barrier.FailureCount,"workflow.activity.async_dependency_blocked",false,true,token).ConfigureAwait(false);return false;}
        if(calls.Any(call=>call.StatusKey=="cancelled"))
        {await YieldAsync(barrier,barrier.FailureCount,"workflow.activity.async_dependency_cancelled",true,true,token).ConfigureAwait(false);return true;}
        var changed=false;
        foreach(var call in calls.Where(call=>(call.StatusKey=="result_ready" && (call.LastErrorKey is null || call.DueAtUtc<=clock.UtcNow)) ||
            (call.StatusKey=="applying" && call.LeaseExpiresAtUtc<=clock.UtcNow)).Take(4))
            changed|=await connectors.ApplyForBarrierAsync(call,barrier,token).ConfigureAwait(false);
        calls=await CallsAsync(barrier,runtime.Dependencies,token).ConfigureAwait(false);
        if(ValidCalls(barrier,runtime,calls) && calls.All(call=>call.StatusKey=="applied"))
        {
            if(await ReleaseAsync(barrier,runtime,token).ConfigureAwait(false))return true;
        }
        await YieldAsync(barrier,changed?0:barrier.FailureCount,null,false,false,token).ConfigureAwait(false);
        return changed;
    }

    private async Task<BarrierRuntime?> LoadRuntimeAsync(WorkflowInstanceRecord instance,WorkflowAsyncBarrierRecord barrier,CancellationToken token)
    {
        if(Hash(barrier.InputJson)!=barrier.InputHash || Hash(barrier.DependencyCallIdsJson)!=barrier.DependencyHash || Encoding.UTF8.GetByteCount(barrier.InputJson)>131072 ||
            Encoding.UTF8.GetByteCount(barrier.DependencyCallIdsJson)>8192)return null;
        var asset=await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            Parameters(("DefinitionVersionId",instance.DefinitionVersionId),("TenantScopeKey",instance.TenantScopeKey)),token).ConfigureAwait(false);
        if(asset is null || asset.FormVersionId!=instance.FormVersionId || !WorkflowDesignerRelease.AllowsExecution(await WorkflowDesignerRelease.ReadBindingsAsync(query,
            asset.FormVersionId,new WorkflowManagementScope(instance.TenantId,instance.ScopeKey,instance.TenantScopeKey),token).ConfigureAwait(false)))return null;
        try
        {
            var dependencies=JsonSerializer.Deserialize(barrier.DependencyCallIdsJson,WorkflowJsonSerializerContext.Default.GuidArray);
            var draft=JsonSerializer.Deserialize(asset.CanonicalJson,WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
            var schema=JsonSerializer.Deserialize(asset.FormSchemaJson,WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            var input=JsonSerializer.Deserialize(barrier.InputJson,WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if(dependencies is null || dependencies.Length>WorkflowDefinitionCompiler.MaximumNodes || dependencies.Any(id=>id==Guid.Empty) || dependencies.Distinct().Count()!=dependencies.Length ||
                draft is null || schema is null || input is null || !WorkflowFormValueValidator.Validate(schema,input) ||
                !WorkflowRuntimePlan.TryCreate(draft,schema,out var plan) || !plan!.TryGetAsyncWait(barrier.NodeKey,out var definition))return null;
            return new(schema,dependencies,definition!);
        }
        catch(Exception error) when(error is JsonException or ArgumentException or InvalidOperationException){return null;}
    }

    private Task<IReadOnlyList<WorkflowConnectorCallRecord>> CallsAsync(WorkflowAsyncBarrierRecord barrier,Guid[] dependencies,CancellationToken token)=>
        dependencies.Length==0?Task.FromResult<IReadOnlyList<WorkflowConnectorCallRecord>>([]):query.QueryAsync<WorkflowConnectorCallRecord>(WorkflowConnectorSql.FindByIds,
            Parameters(("InstanceId",barrier.InstanceId),("TenantScopeKey",barrier.TenantScopeKey),("CallIds",dependencies)),token);

    private static bool ValidCalls(WorkflowAsyncBarrierRecord barrier,BarrierRuntime runtime,IReadOnlyList<WorkflowConnectorCallRecord> calls)=>
        calls.Count==runtime.Dependencies.Length && calls.All(call=>runtime.Dependencies.Contains(call.Id) && call.InstanceId==barrier.InstanceId && call.TenantScopeKey==barrier.TenantScopeKey &&
            call.ModeKey=="async" && call.ParallelJoinId==barrier.ParallelJoinId && call.ParallelBranchKey==barrier.ParallelBranchKey && runtime.Definition.ProducerNodeKeys.Contains(call.NodeKey,StringComparer.Ordinal)) &&
        calls.GroupBy(call=>call.NodeKey,StringComparer.Ordinal).All(group=>group.Count()==1) &&
        (runtime.Definition.IsImplicitBoundary || calls.Count==runtime.Definition.ProducerNodeKeys.Count);

    private async Task<bool> ReleaseAsync(WorkflowAsyncBarrierRecord barrier,BarrierRuntime runtime,CancellationToken token)
    {
        var instance=await InstanceAsync(barrier,token).ConfigureAwait(false);
        if(!Trusted(instance,barrier) || instance!.StatusKey!="active")return false;
        var snapshot=await outputs.ReadSnapshotAsync(instance,barrier.ParallelJoinId,barrier.ParallelBranchKey,runtime.Schema,token).ConfigureAwait(false);
        if(snapshot is null)
        {
            // 依赖已全部接纳后，缺失或不符合Schema的当前表单不能靠轮询自行恢复。
            await YieldAsync(barrier,barrier.FailureCount,"workflow.activity.async_input_invalid",true,true,token).ConfigureAwait(false);
            return true;
        }
        var input=new WorkflowAutomaticInput(barrier.Id,snapshot.Json,snapshot.Hash,snapshot.FormRevision,barrier.ParallelJoinId,barrier.ParallelBranchKey);
        var released=await transaction.ExecuteResultAsync(async inner=>{
            var current=await InstanceAsync(barrier,inner).ConfigureAwait(false);
            if(!Trusted(current,barrier) || current!.StatusKey!="active" || !await LockAsync(current,inner).ConfigureAwait(false))return Invalid<bool>();
            var active=await FindAsync(barrier,inner).ConfigureAwait(false);
            if(!Owns(active,barrier) || !await outputs.MatchesCurrentAsync(current,input,snapshot.Values,inner).ConfigureAwait(false))return Invalid<bool>();
            var calls=await CallsAsync(barrier,runtime.Dependencies,inner).ConfigureAwait(false);
            if(!ValidCalls(barrier,runtime,calls) || calls.Any(call=>call.StatusKey!="applied"))return Invalid<bool>();
            var now=clock.UtcNow;var workId=ids.NewId();var json=WorkflowActivityScheduler.SerializeInput(snapshot.Values);var hash=Hash(json);
            var sequence=await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,Parameters(("InstanceId",current.Id)),inner).ConfigureAwait(false);
            await scheduler.EnqueueAsync(current,barrier.NodeKey,snapshot.Values,snapshot.FormRevision,sequence,barrier.ParallelJoinId,barrier.ParallelBranchKey,now,inner,
                workId:workId,resumeBarrierId:barrier.Id).ConfigureAwait(false);
            if(await commands.ExecuteAsync(WorkflowAsyncBarrierSql.Release,Parameters(("Id",barrier.Id),("InstanceId",barrier.InstanceId),("TenantScopeKey",barrier.TenantScopeKey),
                    ("Generation",barrier.LeaseGeneration),("Owner",barrier.LeaseOwnerKey),("WorkId",workId),("NodeKey",barrier.NodeKey),("OutputJson",json),("OutputHash",hash),
                    ("OutputFormRevision",snapshot.FormRevision),("Now",now)),inner).ConfigureAwait(false)!=1 ||
                await commands.ExecuteAsync(WorkflowSql.CompleteStepWithRevision,Parameters(("Id",barrier.Id),("InstanceId",barrier.InstanceId),("Revision",1L),
                    ("StatusKey","completed"),("CompletedAtUtc",now)),inner).ConfigureAwait(false)!=1 ||
                await commands.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,Parameters(("Id",current.Id),("TenantScopeKey",current.TenantScopeKey),("Revision",current.Revision)),inner).ConfigureAwait(false)!=1)return Invalid<bool>();
            await AuditAsync(current,barrier,"released",null,inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        },token).ConfigureAwait(false);
        return released.IsSuccess;
    }

    private async Task<bool> YieldAsync(WorkflowAsyncBarrierRecord barrier,int failures,string? error,bool hardFailure,bool suspend,CancellationToken token)
    {
        var failed=hardFailure || failures>=8;failures=Math.Clamp(failures,0,8);
        var yielded=await transaction.ExecuteResultAsync(async inner=>{
            var instance=await InstanceAsync(barrier,inner).ConfigureAwait(false);
            if(!Trusted(instance,barrier) || instance!.StatusKey is "completed" or "rejected" or "cancelled" || !await LockAsync(instance,inner).ConfigureAwait(false))return Invalid<bool>();
            if(!Owns(await FindAsync(barrier,inner).ConfigureAwait(false),barrier))return Invalid<bool>();
            if(await commands.ExecuteAsync(WorkflowAsyncBarrierSql.Yield,Parameters(("Id",barrier.Id),("InstanceId",barrier.InstanceId),("TenantScopeKey",barrier.TenantScopeKey),
                ("Generation",barrier.LeaseGeneration),("Owner",barrier.LeaseOwnerKey),("FailureCount",failures),("StatusKey",failed?"failed":"pending"),("ErrorKey",error),
                ("DueAtUtc",clock.UtcNow.AddMilliseconds(Math.Max(1000,options.Value.PollMilliseconds))),("Now",clock.UtcNow)),inner).ConfigureAwait(false)!=1)return Invalid<bool>();
            if((failed || suspend) && instance.StatusKey=="active" && await commands.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision,
                Parameters(("Id",instance.Id),("TenantScopeKey",instance.TenantScopeKey),("Revision",instance.Revision)),inner).ConfigureAwait(false)!=1)return Invalid<bool>();
            if(error is not null && (failed || suspend || failures>barrier.FailureCount))await AuditAsync(instance,barrier,failed?"failed":"blocked",error,inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        },token).ConfigureAwait(false);
        return yielded.IsSuccess;
    }

    private async Task AuditAsync(WorkflowInstanceRecord instance,WorkflowAsyncBarrierRecord barrier,string phase,string? error,CancellationToken token)
    {
        var correlation=LogCorrelationContext.Capture().ToJson();var now=clock.UtcNow;
        var detail=WorkflowJsonCanonicalizer.Compile(writer=>{writer.WriteStartObject();writer.WriteString("nodeTypeKey","internal.await");writer.WriteString("phaseKey",phase);
            writer.WriteString("consumerNodeKey",barrier.NodeKey);writer.WriteString("errorKey",error);writer.WriteEndObject();}).CanonicalJson;
        await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit,Parameters(("Id",ids.NewId()),("TenantId",instance.TenantId),("ScopeKey",instance.ScopeKey),("InstanceId",instance.Id),
            ("OperationKey","activity.execute"),("ActorUserId",null),("ResourceTypeKey","step"),("ResourceId",barrier.Id),("OutcomeKey",error is null?"succeeded":"failed"),
            ("DetailJson",detail),("CreatedAtUtc",now),("CorrelationJson",correlation)),token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowSql.InsertExecutionLog,Parameters(("Id",ids.NewId()),("InstanceId",instance.Id),("StepId",barrier.Id),("CorrelationJson",correlation),
            ("TransitionKey",$"activity.async.{phase}"),("FromStatusKey","active"),("ToStatusKey",phase=="released"?"completed":"active"),
            ("IdempotencyKey",null),("Summary",error),("CreatedAtUtc",now)),token).ConfigureAwait(false);
    }
    private Task<WorkflowInstanceRecord?> InstanceAsync(WorkflowAsyncBarrierRecord row,CancellationToken token)=>query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
        Parameters(("Id",row.InstanceId),("TenantScopeKey",row.TenantScopeKey)),token);
    private Task<WorkflowAsyncBarrierRecord?> FindAsync(WorkflowAsyncBarrierRecord row,CancellationToken token)=>query.QuerySingleOrDefaultAsync<WorkflowAsyncBarrierRecord>(WorkflowAsyncBarrierSql.Find,
        Parameters(("Id",row.Id),("InstanceId",row.InstanceId),("TenantScopeKey",row.TenantScopeKey)),token);
    private async Task<bool> LockAsync(WorkflowInstanceRecord instance,CancellationToken token)=>await commands.ExecuteAsync(WorkflowConnectorSql.LockInstance,
        Parameters(("Id",instance.Id),("TenantScopeKey",instance.TenantScopeKey),("Revision",instance.Revision)),token).ConfigureAwait(false)==1;
    private bool Owns(WorkflowAsyncBarrierRecord? current,WorkflowAsyncBarrierRecord claimed)=>current is{StatusKey:"applying"} && current.LeaseGeneration==claimed.LeaseGeneration &&
        current.LeaseOwnerKey==claimed.LeaseOwnerKey && current.LeaseExpiresAtUtc>clock.UtcNow;
    private static bool Trusted(WorkflowInstanceRecord? instance,WorkflowAsyncBarrierRecord row)=>instance is not null && instance.Id==row.InstanceId && instance.TenantScopeKey==row.TenantScopeKey &&
        instance.TenantScopeKey==(instance.TenantId is{ } id?$"tenant:{id:N}":"host") && instance.ScopeKey==(instance.TenantId is null?"host":"tenant");
    private static string Hash(string json)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static IReadOnlyDictionary<string,object?> Parameters(params(string Key,object? Value)[] values)=>WorkflowSqlParameters.Create(values);
    private static Result<T> Invalid<T>()=>Result<T>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
    private sealed record BarrierRuntime(WorkflowFormSchema Schema,Guid[] Dependencies,WorkflowAsyncWaitDefinition Definition);
}
