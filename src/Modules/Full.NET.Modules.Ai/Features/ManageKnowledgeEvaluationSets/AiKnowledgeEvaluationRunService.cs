using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features.SearchKnowledge;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ai.Streaming;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeEvaluationSets;

/// <summary>不可变策略与原身份运行；输入和预算计划先持久化，任何外部模型调用均在事务外。</summary>
internal sealed partial class AiKnowledgeEvaluationRunService(IQueryExecutor query,ICommandExecutor commands,ICommandTransaction transaction,
    IDataTransactionState transactions,ICurrentTenant tenant,IOptions<DatabaseOptions> database,IClock clock,IIdGenerator ids,
    AiKnowledgeEvaluationRunAccess authority,AiKnowledgeEvaluationOperationScope operations,AiKnowledgePermissionGuard permissions,
    AiKnowledgeSearchComparisonService comparison,AiKnowledgeSearchRefinementService refinement,ICurrentSessionAuthorization authorization,AiChatCleanupScope cleanup)
{
    internal Task<Result<AiKnowledgeEvaluationEntryPage>> EntriesAsync(Guid knowledge,Guid? after,Guid actor,bool runs,CancellationToken token)=>Run(async ()=>
    {
        Key(knowledge);if(after.HasValue)Key(after.Value);var scope=tenant.Id;var scoped=AiKnowledgeScope.IsTenant(tenant);
        var session=await authorization.AuthorizeAllAsync([AiKnowledgePermissions.Read,AiKnowledgeEvaluationPermissions.Read,AiKnowledgeEvaluationRunPermissions.Read],token).ConfigureAwait(false);
        if(session?.UserId!=actor||session.TenantId!=scope||scope!=tenant.Id||scoped!=AiKnowledgeScope.IsTenant(tenant))throw Denied();
        var p=Parameters(knowledge);p["Actor"]=actor;p["AfterId"]=after;
        var rows=await query.QueryAsync<AiKnowledgeEvaluationEntry>(AiKnowledgeEvaluationRunSql.For(database.Value.Provider,scoped,runs?"run_entries":"strategy_entries"),p,token).ConfigureAwait(false);
        if(rows.Count>9||scope!=tenant.Id||scoped!=AiKnowledgeScope.IsTenant(tenant))throw Unavailable();
        return new AiKnowledgeEvaluationEntryPage(rows.Take(8).ToArray(),rows.Count>8?rows[7].Id:null);
    });
    internal Task<Result<AiKnowledgeEvaluationStrategy>> SaveStrategyAsync(Guid knowledge,Guid strategy,Guid actor,
        CaptureAiKnowledgeEvaluationStrategy request,CancellationToken token)=>Run(async ()=>
    {
        Key(strategy);Key(request.RequestKey);if(request.ExpectedVersion<0)throw Invalid();
        var context=await authority.CaptureAsync(knowledge,actor,AiKnowledgeEvaluationRunPermissions.Plan,request,token).ConfigureAwait(false);
        var p=await authority.CurrentAsync(context,AiKnowledgeEvaluationRunPermissions.Plan,token).ConfigureAwait(false);
        p["StrategyId"]=strategy;p["RequestKey"]=request.RequestKey;
        var definition=new AiKnowledgeEvaluationStrategyDefinition(1,knowledge,request.Name.Trim(),request.Baseline,request.Candidate,
            context.Set.Id,context.Set.Revision.Id,context.Set.Version,context.Set.ApprovalId!.Value,context.Set.Revision.ContentHash,context.Set.Revision.SourceHash,
            AiKnowledgeEvaluationRunAccess.Model(context.Embedding,"embedding"),context.Processing is null?null:AiKnowledgeEvaluationRunAccess.Model(context.Processing,"processing"),
            context.Sources.Sources.Select(row=>row.DocumentId).ToArray(),context.Sources.Sources.Select(row=>AiKnowledgeSearchSources.Capture(row,context.Sources.FaqSources.GetValueOrDefault(row.DocumentId))).ToArray(),
            AiKnowledgeEvaluationRunPolicy.PromptHash,AiKnowledgeEvaluationRunPolicy.MeasurementProtocol);
        var json=AiKnowledgeEvaluationRunPolicy.Encode(definition);var hash=Hash("strategy",actor,strategy,request.ExpectedVersion+"\n"+json);
        var replay=await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationStrategyRow>(Sql(context,"strategy_replay"),p,token).ConfigureAwait(false);
        if(replay is not null){if(replay.RequestHash!=hash)throw Conflict();await authority.CurrentAsync(context,AiKnowledgeEvaluationRunPermissions.Plan,token).ConfigureAwait(false);return Map(replay);}
        var old=await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationStrategyRow>(Sql(context,"strategy_head"),p,token).ConfigureAwait(false);
        if(old is null?request.ExpectedVersion!=0:old.Version!=request.ExpectedVersion)throw Conflict();
        p["StrategyRevisionId"]=NewId();p["RevisionNo"]=old is null?1:old.HeadRevisionNo+1;p["ExpectedStrategyVersion"]=request.ExpectedVersion;
        p["RequestHash"]=hash;p["DefinitionJson"]=json;p["DefinitionHash"]=AiKnowledgeEvaluationPolicy.Hash(json);p["Name"]=definition.Name;
        p["Now"]=Now();p["Actor"]=actor;
        await WriteAsync(context,p,async ct=>
        {
            var current=await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationStrategyRow>(Sql(context,"strategy_replay"),p,ct).ConfigureAwait(false);
            if(current is not null){if(current.RequestHash!=hash)throw Conflict();p["StrategyRevisionId"]=current.RevisionId;return;}
            if(old is null)await Execute("insert_strategy",p,context,ct).ConfigureAwait(false);
            else await Execute("advance_strategy",p,context,ct).ConfigureAwait(false);
            await Execute("append_strategy",p,context,ct).ConfigureAwait(false);
        },token).ConfigureAwait(false);
        return await StrategyAsync(knowledge,strategy,(Guid)p["StrategyRevisionId"]!,actor,AiKnowledgeEvaluationRunPermissions.Plan,token).ConfigureAwait(false);
    });

    internal Task<Result<AiKnowledgeEvaluationStrategy>> GetStrategyAsync(Guid knowledge,Guid strategy,Guid? revision,Guid actor,CancellationToken token)=>
        Run(()=>StrategyAsync(knowledge,strategy,revision,actor,AiKnowledgeEvaluationRunPermissions.Read,token));

    private async Task<AiKnowledgeEvaluationStrategy> StrategyAsync(Guid knowledge,Guid strategy,Guid? revision,Guid actor,string permission,CancellationToken token)
    {
        var row=await StrategyRowAsync(knowledge,strategy,revision,token).ConfigureAwait(false);
        _=await authority.ProveAsync(actor,permission,Definition(row),permission!=AiKnowledgeEvaluationRunPermissions.Read,token).ConfigureAwait(false);
        return Map(row);
    }

    internal Task<Result<AiKnowledgeEvaluationRunAdmission>> AdmissionAsync(Guid knowledge,Guid strategy,Guid revision,Guid actor,CancellationToken token)=>Run(async ()=>
    {
        var row=await StrategyRowAsync(knowledge,strategy,revision,token).ConfigureAwait(false);var definition=Definition(row);
        var context=await authority.ProveAsync(actor,AiKnowledgeEvaluationRunPermissions.Create,definition,true,token).ConfigureAwait(false);
        return Admission(row,context.Set.Revision.Cases.Count);
    });

    internal Task<Result<AiKnowledgeEvaluationRun>> CreateRunAsync(Guid knowledge,Guid strategy,Guid actor,CreateAiKnowledgeEvaluationRun request,CancellationToken token)=>Run(async ()=>
    {
        Key(request.RequestKey);if(!request.ConfirmPaidModelCalls)throw Invalid();
        var row=await StrategyRowAsync(knowledge,strategy,request.StrategyRevisionId,token).ConfigureAwait(false);var definition=Definition(row);
        var context=await authority.ProveAsync(actor,AiKnowledgeEvaluationRunPermissions.Create,definition,true,token).ConfigureAwait(false);
        var admission=Admission(row,context.Set.Revision.Cases.Count);if(admission.Digest!=request.AdmissionDigest)throw Conflict();
        var p=await authority.CurrentAsync(context,AiKnowledgeEvaluationRunPermissions.Create,token).ConfigureAwait(false);
        p["RequestKey"]=request.RequestKey;p["RequestHash"]=Hash("run",actor,strategy,request.StrategyRevisionId+"\n"+request.AdmissionDigest);
        var replay=await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRunRow>(Sql(context,"run_replay"),p,token).ConfigureAwait(false);
        if(replay is not null){if(replay.RequestHash!=(string)p["RequestHash"]!)throw Conflict();return await ReadRunAsync(replay,actor,token).ConfigureAwait(false);}
        var cases=context.Set.Revision.Cases.Select((sample,index)=>new AiKnowledgeEvaluationPlannedCase(NewId(),sample.Id,index,sample.Sample,
            AiKnowledgeEvaluationRunPolicy.Stages(definition.Candidate).Select(stage=>new AiKnowledgeEvaluationStage(NewId(),stage)).ToArray())).ToArray();
        var plan=new AiKnowledgeEvaluationRunPlan(1,strategy,row.RevisionId,row.DefinitionHash,cases);var json=AiKnowledgeEvaluationRunPolicy.Encode(plan);
        p["RunId"]=NewId();p["StrategyId"]=strategy;p["StrategyRevisionId"]=row.RevisionId;p["Actor"]=actor;p["SessionId"]=context.SessionId;
        p["PlanJson"]=json;p["PlanHash"]=AiKnowledgeEvaluationPolicy.Hash(json);p["Now"]=Now();
        await WriteAsync(context,p,async ct=>
        {
            var existing=await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRunRow>(Sql(context,"run_replay"),p,ct).ConfigureAwait(false);
            if(existing is not null){if(existing.RequestHash!=(string)p["RequestHash"]!)throw Conflict();p["RunId"]=existing.Id;return;}
            await Execute("insert_run",p,context,ct).ConfigureAwait(false);
            foreach(var item in cases)
            {
                p["CaseId"]=item.Id;p["SampleId"]=item.SampleId;p["Sequence"]=item.Sequence;await Execute("insert_case",p,context,ct).ConfigureAwait(false);
                foreach(var stage in item.Operations)
                {
                    var model=stage.Stage is "refinement_rewrite" or "refinement_rerank"?definition.ProcessingModel!:definition.EmbeddingModel;
                    p["OperationId"]=stage.Id;p["StageKey"]=stage.Stage;p["ModelConfigId"]=model.Id;p["ModelConfigVersion"]=model.Version;
                    await Execute("insert_operation",p,context,ct).ConfigureAwait(false);
                }
            }
            // 循环写入用的模型参数不能覆盖最终完整来源门禁的原批准模型。
            p["ModelConfigId"]=context.Embedding.Id;p["ModelConfigVersion"]=context.Embedding.Version;
        },token).ConfigureAwait(false);
        var saved=await RunRowAsync(knowledge,(Guid)p["RunId"]!,token).ConfigureAwait(false);return await ReadRunAsync(saved,actor,token).ConfigureAwait(false);
    });

    internal Task<Result<AiKnowledgeEvaluationRun>> GetRunAsync(Guid knowledge,Guid run,Guid actor,CancellationToken token)=>Run(async ()=>
        await ReadRunAsync(await RunRowAsync(knowledge,run,token).ConfigureAwait(false),actor,token).ConfigureAwait(false));

    private async Task<AiKnowledgeEvaluationRun> ReadRunAsync(AiKnowledgeEvaluationRunRow run,Guid actor,CancellationToken token)
    {
        var strategy=await StrategyRowAsync(run.KnowledgeBaseId,run.StrategyId,run.StrategyRevisionId,token).ConfigureAwait(false);
        var context=await authority.ProveAsync(actor,AiKnowledgeEvaluationRunPermissions.Read,Definition(strategy),false,token).ConfigureAwait(false);
        var plan=Plan(run,strategy);var p=Parameters(run.KnowledgeBaseId);p["RunId"]=run.Id;
        var rows=await query.QueryAsync<AiKnowledgeEvaluationCaseRow>(Sql(context,"cases"),p,token).ConfigureAwait(false);
        if(rows.Count!=plan.Cases.Count||plan.Cases.Any(item=>rows.Count(row=>row.Id==item.Id&&row.SampleId==item.SampleId&&row.Sequence==item.Sequence)!=1))throw Unavailable();
        var states=rows.Select(State).ToArray();var duration=AiKnowledgeEvaluationRunPolicy.Duration(states);
        await authority.CurrentAsync(context,AiKnowledgeEvaluationRunPermissions.Read,token).ConfigureAwait(false);
        int Count(string value)=>states.Count(item=>item.State==value);
        return new(run.Id,run.KnowledgeBaseId,run.StrategyId,run.StrategyRevisionId,context.Set.Revision.Id,run.Version,
            Count("running")>0?"running":Count("not_run")>0?"pending":"completed",states.Length,Count("succeeded"),Count("failed"),Count("unknown"),Count("skipped"),Count("not_run"),Count("running"),
            duration.Measured,duration.Mean,duration.P95,states,run.CreatedAtUtc);
    }

    private async Task WriteAsync(AiKnowledgeEvaluationRunAccess.Context context,Dictionary<string,object?> p,Func<CancellationToken,Task> action,CancellationToken token)
    {
        using var phase=CancellationTokenSource.CreateLinkedTokenSource(token);phase.CancelAfter(TimeSpan.FromSeconds(25));
        await transaction.ExecuteAsync(async ct=>
        {
            if(context.TenantId!=tenant.Id||context.Scoped!=AiKnowledgeScope.IsTenant(tenant))throw Denied();
            if(await query.QuerySingleOrDefaultAsync<Guid?>(AiKnowledgeEvaluationSql.LockFor(database.Value.Provider,context.Scoped),p,ct).ConfigureAwait(false) is null)throw Unavailable();
            await CurrentInTransactionAsync(context,p,ct).ConfigureAwait(false);
            await action(ct).ConfigureAwait(false);
            await CurrentInTransactionAsync(context,p,ct).ConfigureAwait(false);
            return true;
        },phase.Token).ConfigureAwait(false);
    }
    private async Task CurrentInTransactionAsync(AiKnowledgeEvaluationRunAccess.Context context,Dictionary<string,object?> p,CancellationToken token)
    {
        var rows=await query.QueryAsync<AiKnowledgeIndexStatusRecord>(Sql(context,"current"),p,token).ConfigureAwait(false);
        if(rows.Count!=context.Sources.Sources.Count||context.Sources.Sources.Any(expected=>rows.Count(row=>row==expected)!=1))throw Unavailable();
    }
    private Task<int> Execute(string purpose,Dictionary<string,object?> p,AiKnowledgeEvaluationRunAccess.Context context,CancellationToken token)=>
        ExecuteOne(Sql(context,purpose),p,token);
    private async Task<int> ExecuteOne(SqlStatement sql,Dictionary<string,object?> p,CancellationToken token)
    {var count=await commands.ExecuteAsync(sql,p,token).ConfigureAwait(false);if(count!=1)throw Conflict();return count;}
    private SqlStatement Sql(AiKnowledgeEvaluationRunAccess.Context context,string purpose)=>AiKnowledgeEvaluationRunSql.For(database.Value.Provider,context.Scoped,purpose);
    private Dictionary<string,object?> Parameters(Guid knowledge)=>new(){["KnowledgeBaseId"]=knowledge};
    private async Task<AiKnowledgeEvaluationStrategyRow> StrategyRowAsync(Guid knowledge,Guid strategy,Guid? revision,CancellationToken token)
    {
        Key(knowledge);Key(strategy);if(revision.HasValue)Key(revision.Value);var p=Parameters(knowledge);p["StrategyId"]=strategy;p["StrategyRevisionId"]=revision;
        return await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationStrategyRow>(AiKnowledgeEvaluationRunSql.For(database.Value.Provider,AiKnowledgeScope.IsTenant(tenant),revision.HasValue?"strategy":"strategy_head"),p,token).ConfigureAwait(false)??throw Missing();
    }
    private async Task<AiKnowledgeEvaluationRunRow> RunRowAsync(Guid knowledge,Guid run,CancellationToken token)
    {Key(knowledge);Key(run);var p=Parameters(knowledge);p["RunId"]=run;return await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationRunRow>(AiKnowledgeEvaluationRunSql.For(database.Value.Provider,AiKnowledgeScope.IsTenant(tenant),"run"),p,token).ConfigureAwait(false)??throw Missing();}
    private static AiKnowledgeEvaluationStrategyDefinition Definition(AiKnowledgeEvaluationStrategyRow row)
    {if(AiKnowledgeEvaluationPolicy.Hash(row.DefinitionJson)!=row.DefinitionHash)throw Unavailable();var value=AiKnowledgeEvaluationRunPolicy.Definition(row.DefinitionJson);if(value.KnowledgeBaseId!=row.KnowledgeBaseId||value.PromptHash!=AiKnowledgeEvaluationRunPolicy.PromptHash)throw Unavailable();return value;}
    private static AiKnowledgeEvaluationRunPlan Plan(AiKnowledgeEvaluationRunRow row,AiKnowledgeEvaluationStrategyRow strategy)
    {if(AiKnowledgeEvaluationPolicy.Hash(row.PlanJson)!=row.PlanHash)throw Unavailable();var value=AiKnowledgeEvaluationRunPolicy.Plan(row.PlanJson);if(value.StrategyId!=strategy.Id||value.StrategyRevisionId!=strategy.RevisionId||value.DefinitionHash!=strategy.DefinitionHash)throw Unavailable();return value;}
    private static AiKnowledgeEvaluationStrategy Map(AiKnowledgeEvaluationStrategyRow row)
    {var d=Definition(row);return new(row.Id,row.RevisionId,row.Version,row.KnowledgeBaseId,d.Name,d.MeasurementProtocol,d.Baseline,d.Candidate,d.SetId,d.SetRevisionId,d.SetVersion,d.SetApprovalId,d.SetContentHash,d.SetSourceHash,d.EmbeddingModel,d.ProcessingModel,d.Sources,row.DefinitionHash,row.CreatedAtUtc);}
    private static AiKnowledgeEvaluationRunAdmission Admission(AiKnowledgeEvaluationStrategyRow row,int count)=>new(row.Id,row.RevisionId,row.Version,Definition(row).SetRevisionId,
        AiKnowledgeEvaluationPolicy.Hash(row.Id+"\n"+row.RevisionId+"\n"+row.Version+"\n"+row.DefinitionHash+"\n"+count),count,count*AiKnowledgeEvaluationRunPolicy.CallsPerCase(Definition(row).Candidate),"existing_per_operation_budget_v1",AiKnowledgeEvaluationRunPolicy.MeasurementProtocol);
    private static AiKnowledgeEvaluationCaseState State(AiKnowledgeEvaluationCaseRow row)
    {
        if(row.Version<1||row.Sequence is <0 or >=8||row.StateKey is not("not_run" or "running" or "succeeded" or "failed" or "unknown" or "skipped")
            ||row.StateKey=="succeeded"&&(row.ResultJson is null||row.ResultHash is null)
            ||row.StateKey!="succeeded"&&(row.ResultJson is not null||row.ResultHash is not null)
            ||row.StateKey=="not_run"&&(row.DurationMs.HasValue||row.StartedAtUtc.HasValue||row.FinishedAtUtc.HasValue)
            ||row.StateKey=="running"&&(row.ExecutionToken is null||row.StartedAtUtc is null||row.FinishedAtUtc.HasValue))throw Unavailable();
        return new(row.Id,row.SampleId,row.Sequence,row.Version,row.StateKey,row.ReasonCode,row.DurationMs.HasValue?(double)row.DurationMs.Value:null,
            row.StartedAtUtc,row.FinishedAtUtc,row.BaselineSupports,row.CandidateSupports);
    }
    private async Task<Result<T>> Run<T>(Func<Task<T>> action)
    {
        if(transactions.HasTransaction)throw new InvalidOperationException("实际评估不能加入已有事务。");
        try{return Result<T>.Success(await action().ConfigureAwait(false));}
        catch(AiKnowledgeEvaluationRunAccessException error){return Result<T>.Failure(new(error.Code,"The evaluation run is unavailable or invalid.",error.Type));}
        catch(AiKnowledgeSearchSourceChangedException){return Result<T>.Failure(new("ai.knowledge_evaluation_runs.source_unavailable","The frozen evaluation source has changed.",ErrorType.Conflict));}
        catch(UnauthorizedAccessException){return Result<T>.Failure(new("authorization.permission_denied","The operation is not authorized.",ErrorType.Forbidden));}
        catch(OperationCanceledException){return Result<T>.Failure(new("ai.knowledge_evaluation_runs.source_unavailable","The evaluation phase did not complete.",ErrorType.Conflict));}
        catch(DataCommandException error)when(error.Kind is DataCommandFailureKind.Deadlock or DataCommandFailureKind.UniqueConstraint){return Result<T>.Failure(new("ai.knowledge_evaluation_runs.version_conflict","The evaluation run conflicted.",ErrorType.Conflict));}
        catch(Exception error)when(error is InvalidDataException or ArgumentException or System.Text.Json.JsonException){return Result<T>.Failure(new("ai.knowledge_evaluation_runs.input_invalid","The evaluation run input is invalid.",ErrorType.Validation));}
    }
    private Guid NewId(){var id=ids.NewId();Key(id);return id;}
    private static void Key(Guid id){if(id==Guid.Empty||id.Version!=7)throw Invalid();}
    private DateTimeOffset Now(){var now=clock.UtcNow;return new(now.UtcTicks-now.UtcTicks%10,TimeSpan.Zero);}
    private static string Hash(string purpose,Guid actor,Guid root,string value)=>AiKnowledgeEvaluationPolicy.Hash(purpose+"\n"+actor+"\n"+root+"\n"+value);
    private static AiKnowledgeEvaluationRunAccessException Invalid()=>new("ai.knowledge_evaluation_runs.input_invalid",ErrorType.Validation);
    private static AiKnowledgeEvaluationRunAccessException Conflict()=>new("ai.knowledge_evaluation_runs.version_conflict",ErrorType.Conflict);
    private static AiKnowledgeEvaluationRunAccessException Unavailable()=>new("ai.knowledge_evaluation_runs.source_unavailable",ErrorType.Conflict);
    private static AiKnowledgeEvaluationRunAccessException Missing()=>new("ai.knowledge_evaluation_runs.not_found",ErrorType.NotFound);
    private static AiKnowledgeEvaluationRunAccessException Denied()=>new("authorization.permission_denied",ErrorType.Forbidden);
}
