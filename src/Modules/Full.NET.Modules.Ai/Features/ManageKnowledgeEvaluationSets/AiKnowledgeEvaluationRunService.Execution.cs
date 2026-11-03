using System.Diagnostics;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeEvaluationSets;

/// <summary>每个样例领取一次；失败、取消或未知结算不自动重派，不用新的操作身份掩盖旧状态。</summary>
internal sealed partial class AiKnowledgeEvaluationRunService
{
    internal Task<Result<AiKnowledgeEvaluationCaseResult>> CaseAsync(Guid knowledge,Guid run,Guid item,Guid actor,CancellationToken token)=>Run(async ()=>
        await ReadCaseAsync(await RunRowAsync(knowledge,run,token).ConfigureAwait(false),item,actor,token).ConfigureAwait(false));

    internal Task<Result<AiKnowledgeEvaluationCaseResult>> ActAsync(Guid knowledge,Guid runId,Guid caseId,Guid actor,
        ExecuteAiKnowledgeEvaluationCase request,string purpose,CancellationToken token)=>Run(async ()=>
    {
        Key(caseId);Key(request.RequestKey);if(request.ExpectedVersion<1||purpose is not("execute" or "skip" or "close_unknown"))throw Invalid();
        var permission=purpose switch{"execute"=>AiKnowledgeEvaluationRunPermissions.Execute,"skip"=>AiKnowledgeEvaluationRunPermissions.Skip,_=>AiKnowledgeEvaluationRunPermissions.CloseUnknown};
        var run=await RunRowAsync(knowledge,runId,token).ConfigureAwait(false);
        var strategy=await StrategyRowAsync(knowledge,run.StrategyId,run.StrategyRevisionId,token).ConfigureAwait(false);var definition=Definition(strategy);
        var context=await authority.ProveAsync(actor,permission,definition,true,token).ConfigureAwait(false);
        if(run.CreatedById!=actor||purpose=="execute"&&run.SessionId!=context.SessionId)throw Denied();
        var plan=Plan(run,strategy);var planned=plan.Cases.SingleOrDefault(item=>item.Id==caseId)??throw Missing();
        var p=await authority.CurrentAsync(context,permission,token).ConfigureAwait(false);p["RunId"]=runId;p["CaseId"]=caseId;
        var row=await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationCaseRow>(Sql(context,"case"),p,token).ConfigureAwait(false)??throw Missing();
        var fingerprint=Hash(purpose,actor,caseId,request.ExpectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if(purpose=="close_unknown"&&row.CloseRequestKey==request.RequestKey)
        {if(row.CloseRequestHash!=fingerprint)throw Conflict();return await ReadCaseAsync(run,caseId,actor,token).ConfigureAwait(false);}
        // 收到原请求的重放只读既有结果；绝不重新进入模型作用域。
        if(row.RequestKey==request.RequestKey)
        {if(row.RequestHash!=fingerprint)throw Conflict();return await ReadCaseAsync(run,caseId,actor,token).ConfigureAwait(false);}
        if(row.Version!=request.ExpectedVersion||(purpose=="close_unknown"?row.StateKey!="running":row.StateKey!="not_run"))throw Conflict();
        p["ExpectedVersion"]=context.Set.Version; // 联合门禁使用集合版本，样例CAS另用独立参数。
        p["ExpectedCaseVersion"]=request.ExpectedVersion;p["Actor"]=actor;p["SessionId"]=run.SessionId;
        p["RequestKey"]=request.RequestKey;p["RequestHash"]=fingerprint;p["ExecutionToken"]=NewId();p["Now"]=Now();
        p["StateKey"]=purpose=="execute"?"running":"skipped";p["ReasonCode"]=purpose=="skip"?"operator_skipped":null;
        p["FinishedAtUtc"]=purpose=="skip"?Now():null;
        await WriteAsync(context,p,async ct=>
        {
            await Execute(purpose=="close_unknown"?"close_unknown":"claim",p,context,ct).ConfigureAwait(false);
            await Execute("touch_run",p,context,ct).ConfigureAwait(false);
        },token).ConfigureAwait(false);
        if(purpose!="execute")return await ReadCaseAsync(run,caseId,actor,token).ConfigureAwait(false);
        var originalTenant=context.TenantId is {} tenantId?new TenantContext(tenantId,tenant.Identifier!,tenant.Name!):null;
        var started=Stopwatch.GetTimestamp();AiKnowledgeEvaluationObservedResult? observed=null;string state="unknown";string? reason="execution_interrupted";
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var original=operations.Enter(actor,run.SessionId,context.TenantId,context.Scoped,planned.Operations.ToDictionary(item=>item.Stage,item=>item.Id,StringComparer.Ordinal),
                async stop=>{await authority.CurrentAsync(context,permission,stop).ConfigureAwait(false);});
            var scope=new AiKnowledgeSearchAnswerScope(definition.DocumentIds,definition.EmbeddingModel.Version,definition.Sources);
            var corpus=definition.Sources.Select(source=>source.KnowledgeBaseId).Distinct().ToArray();
            var first=await comparison.CompareAsync(new(corpus,definition.EmbeddingModel.Id,planned.Sample.Question,
                AiKnowledgeEvaluationRunPolicy.HitLimit,false,scope),actor,deadline.Token).ConfigureAwait(false);
            if(!first.IsSuccess){reason=Safe(first.Error?.Code);state="failed";}
            else
            {
                var baseline=definition.Baseline=="keyword"?first.Value!.Keyword:first.Value!.Vector;
                if(definition.Candidate=="vector")observed=new(1,baseline,first.Value!.Vector,null,AiKnowledgeEvaluationRunPolicy.MeasurementProtocol);
                else
                {
                    var next=await refinement.RefineAsync(new(corpus,definition.EmbeddingModel.Id,definition.ProcessingModel!.Id,definition.ProcessingModel.Version,
                        planned.Sample.Question,"rewrite_rerank",AiKnowledgeEvaluationRunPolicy.HitLimit,false,scope),actor,deadline.Token).ConfigureAwait(false);
                    if(!next.IsSuccess){reason=Safe(next.Error?.Code);state="failed";}
                    else observed=new(1,baseline,next.Value!.Reranked,next.Value.RefusedStage is not null,AiKnowledgeEvaluationRunPolicy.MeasurementProtocol)
                        {EffectiveQuery=next.Value.EffectiveQuery};
                }
                if(observed is not null)
                {
                    await authority.CurrentAsync(context,permission,deadline.Token).ConfigureAwait(false);
                    ValidateResult(observed,definition);state="succeeded";reason=null;
                }
            }
        }
        catch(OperationCanceledException){state="unknown";reason="execution_interrupted";observed=null;}
        catch(Exception error)when(error is AiKnowledgeEvaluationRunAccessException or UnauthorizedAccessException or IOException or InvalidDataException or InvalidOperationException or HttpRequestException)
        {state="failed";reason="execution_unavailable";observed=null;}
        finally
        {
            // 请求断开后只做有界安全结算与原令牌状态写入；不会读取或交付私人正文，不会派发模型。
            var completion=new Dictionary<string,object?>(p){["StateKey"]=state,["ReasonCode"]=reason,["ResultJson"]=observed is null?null:AiKnowledgeEvaluationRunPolicy.Encode(observed),
                ["DurationMs"]=(decimal)Stopwatch.GetElapsedTime(started).TotalMilliseconds,["Now"]=Now()};
            completion["ResultHash"]=completion["ResultJson"] is string json?AiKnowledgeEvaluationPolicy.Hash(json):null;
            if(!await cleanup.RunAsync(originalTenant,async(services,ct)=>
            {
                var writer=services.GetRequiredService<ICommandExecutor>();
                await services.GetRequiredService<ICommandTransaction>().ExecuteAsync(async work=>
                {
                    var changed=await writer.ExecuteAsync(Sql(context,"complete"),completion,work).ConfigureAwait(false);
                    if(changed==1&&await writer.ExecuteAsync(Sql(context,"touch_run"),completion,work).ConfigureAwait(false)!=1)throw Conflict();
                    return changed;
                },ct).ConfigureAwait(false);
            }).ConfigureAwait(false))throw Unavailable();
        }
        token.ThrowIfCancellationRequested();return await ReadCaseAsync(run,caseId,actor,token).ConfigureAwait(false);
    });

    private async Task<AiKnowledgeEvaluationCaseResult> ReadCaseAsync(AiKnowledgeEvaluationRunRow run,Guid caseId,Guid actor,CancellationToken token)
    {
        Key(caseId);var strategy=await StrategyRowAsync(run.KnowledgeBaseId,run.StrategyId,run.StrategyRevisionId,token).ConfigureAwait(false);var definition=Definition(strategy);
        var context=await authority.ProveAsync(actor,AiKnowledgeEvaluationRunPermissions.Read,definition,false,token).ConfigureAwait(false);
        var plan=Plan(run,strategy);var planned=plan.Cases.SingleOrDefault(item=>item.Id==caseId)??throw Missing();
        var p=Parameters(run.KnowledgeBaseId);p["RunId"]=run.Id;p["CaseId"]=caseId;p["ScopeKey"]=context.TenantId?.ToString("N")??"host";
        var row=await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationCaseRow>(Sql(context,"case"),p,token).ConfigureAwait(false)??throw Missing();
        AiKnowledgeEvaluationObservedResult? result=null;
        if(row.ResultJson is not null)
        {if(row.StateKey!="succeeded"||AiKnowledgeEvaluationPolicy.Hash(row.ResultJson)!=row.ResultHash)throw Unavailable();result=AiKnowledgeEvaluationRunPolicy.Result(row.ResultJson);ValidateResult(result,definition);}
        IReadOnlyList<AiKnowledgeEvaluationOperation>? ledger=null;var finance="not_authorized";
        if(await permissions.IsAuthorizedAsync([AiKnowledgeFinancePermissions.Read],actor,token).ConfigureAwait(false))
        {
            var records=await query.QueryAsync<AiKnowledgeEvaluationOperationRow>(Sql(context,"operations"),p,token).ConfigureAwait(false);
            if(records.Count!=planned.Operations.Count||records.Any(value=>!planned.Operations.Any(stage=>stage.Id==value.Id&&stage.Stage==value.StageKey)))throw Unavailable();
            ledger=records.Select(value=>new AiKnowledgeEvaluationOperation(value.Id,value.StageKey,value.LedgerId is null?"not_recorded":value.Outcome??"pending",
                value.UsageStatus??"not_recorded",value.QuotaMonthKey??"not_recorded",value.PriceVersionId,value.Currency,value.ReservedCost?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                value.LedgerId.HasValue&&value.UsageStatus=="known"&&value.SettledAtUtc.HasValue?value.ChargedCost?.ToString(System.Globalization.CultureInfo.InvariantCulture):null)).ToArray();finance="available";
        }
        await authority.CurrentAsync(context,AiKnowledgeEvaluationRunPermissions.Read,token).ConfigureAwait(false);
        // 财务读取独立复核，运行资格不隐含范围账单读取权。
        if(ledger is not null&&!await permissions.IsAuthorizedAsync([AiKnowledgeFinancePermissions.Read],actor,token).ConfigureAwait(false))throw Denied();
        if(ledger is not null)await authority.CurrentAsync(context,AiKnowledgeEvaluationRunPermissions.Read,token).ConfigureAwait(false);
        return new(State(row),planned.Sample,result?.Baseline,result?.Candidate,result?.RefinementRefused,AiKnowledgeEvaluationRunPolicy.MeasurementProtocol,finance,ledger)
            {EffectiveQuery=result?.EffectiveQuery};
    }

    internal Task<Result<AiKnowledgeEvaluationCaseResult>> ReviewAsync(Guid knowledge,Guid runId,Guid caseId,Guid actor,ReviewAiKnowledgeEvaluationResult request,CancellationToken token)=>Run(async ()=>
    {
        Key(request.RequestKey);if(!request.ConfirmHumanReview||request.ExpectedVersion<1)throw Invalid();
        var run=await RunRowAsync(knowledge,runId,token).ConfigureAwait(false);var strategy=await StrategyRowAsync(knowledge,run.StrategyId,run.StrategyRevisionId,token).ConfigureAwait(false);
        var context=await authority.ProveAsync(actor,AiKnowledgeEvaluationRunPermissions.Review,Definition(strategy),true,token).ConfigureAwait(false);
        var p=await authority.CurrentAsync(context,AiKnowledgeEvaluationRunPermissions.Review,token).ConfigureAwait(false);p["RunId"]=runId;p["CaseId"]=caseId;p["RequestKey"]=request.RequestKey;
        var hash=Hash("review",actor,caseId,request.ExpectedVersion+"\n"+request.BaselineSupportsReference+"\n"+request.CandidateSupportsReference);
        var replay=await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationCaseReviewRow>(Sql(context,"review_replay"),p,token).ConfigureAwait(false);
        if(replay is not null){if(replay.RequestHash!=hash)throw Conflict();return await ReadCaseAsync(run,caseId,actor,token).ConfigureAwait(false);}
        var row=await query.QuerySingleOrDefaultAsync<AiKnowledgeEvaluationCaseRow>(Sql(context,"case"),p,token).ConfigureAwait(false)??throw Missing();
        if(row.StateKey!="succeeded"||row.ResultJson is null||row.ResultHash is null||row.Version!=request.ExpectedVersion)throw Conflict();
        var result=AiKnowledgeEvaluationRunPolicy.Result(row.ResultJson);ValidateResult(result,Definition(strategy));
        if(AiKnowledgeEvaluationPolicy.Hash(row.ResultJson)!=row.ResultHash||result.Candidate is null&&request.CandidateSupportsReference)throw Invalid();
        p["ReviewId"]=NewId();p["ResultHash"]=row.ResultHash;p["Actor"]=actor;p["BaselineSupports"]=request.BaselineSupportsReference;p["CandidateSupports"]=request.CandidateSupportsReference;
        p["ExpectedCaseVersion"]=request.ExpectedVersion;p["RequestHash"]=hash;p["Now"]=Now();
        await WriteAsync(context,p,async ct=>{await Execute("review",p,context,ct).ConfigureAwait(false);await Execute("insert_review",p,context,ct).ConfigureAwait(false);await Execute("touch_run",p,context,ct).ConfigureAwait(false);},token).ConfigureAwait(false);
        return await ReadCaseAsync(run,caseId,actor,token).ConfigureAwait(false);
    });

    private static void ValidateResult(AiKnowledgeEvaluationObservedResult value,AiKnowledgeEvaluationStrategyDefinition definition)
    {
        if(value.Baseline is null||value.Candidate is null&&value.RefinementRefused!=true||value.EffectiveQuery is {Length:>4096})throw Unavailable();
        foreach(var rank in new[]{value.Baseline,value.Candidate}.Where(rank=>rank is not null))
        {
            if(rank!.Hits is null||rank.Hits.Count>AiKnowledgeEvaluationRunPolicy.HitLimit)throw Unavailable();
            foreach(var hit in rank.Hits)if(!definition.Sources.Contains(hit.Source)||!double.IsFinite(hit.Score))throw Unavailable();
        }
    }
    private static string Safe(string? code)=>code is "authorization.permission_denied" or "ai.budget.operation_conflict" or "ai.budget.hard_cost_unavailable"?code:"execution_unavailable";
}
