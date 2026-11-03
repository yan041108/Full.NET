using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>公开模型调用、原账本身份及冻结来源；受控输出只证明执行边界，不提升真实云质量。</summary>
internal static class KnowledgeEvaluationRunAssertions
{
    internal static async Task VerifyAsync(HttpClient client,DbConnection db,Guid knowledge,Guid document,Guid embedding,int embeddingVersion,
        Guid processing,int processingVersion,string owner,string reviewer,Func<int> calls,Action<string?,bool> fault,Func<Task> replace,bool nativeHttp=false)
    {
        var root=$"/api/v1/ai/knowledge-bases/{knowledge}";var path=root+"/evaluation-sets";
        await VerifyLocalizedErrorsAsync(client,knowledge,owner);
        // 只配置运维价格，不构造账本；下面所有金额都由实际模型回执和生产预算服务结算。
        foreach(var price in new[]{(Model:embedding,Currency:"USD"),(Model:processing,Currency:"CNY")})
            Assert.AreEqual(1,await db.ExecuteAsync("INSERT INTO fn_ai_model_price (Id,ModelConfigId,ProviderKey,ModelId,Currency,InputPerMillion,OutputPerMillion,CachedInputPerMillion,ValidFromUtc) SELECT @Id,model.Id,model.ProviderKey,model.ModelId,@Currency,2,4,1,@ValidFrom FROM fn_ai_model_config AS model WHERE model.Id=@Model",
                new{Id=Guid.CreateVersion7(),price.Model,price.Currency,ValidFrom=DateTime.UtcNow.AddHours(-1)}));
        var sample=new AiFeedbackEvaluationSample("zh-CN","明确 补写","人工参考答案","人工支持依据",false);
        var dataset=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,path,owner,new SaveAiKnowledgeEvaluationSet(Guid.CreateVersion7(),null,0,"实际运行冻结集",
            [new(null,sample,"独立人工依据",null),new(null,sample with{Question="人工事实"},"另一独立人工依据",null)],true,true))).Workspace!;
        dataset=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,path+$"/{dataset.Id}/approvals",reviewer,new ReviewAiKnowledgeEvaluationSet(Guid.CreateVersion7(),dataset.Revision.Id,dataset.Version,"approved",true))).Workspace!;
        var search=await Send<AiKnowledgeSearchResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-search",owner,
            new SearchAiKnowledgeRequest([knowledge],[document],embedding,embeddingVersion,sample.Question,5,false,null));
        var baselineCalls=calls();var ledgerBefore=await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        var files=await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file");
        var strategies=new List<AiKnowledgeEvaluationStrategy>();var runs=new List<AiKnowledgeEvaluationRun>();
        foreach(var candidate in new[]{"vector","rewrite_rerank"})
        {
            var id=Guid.CreateVersion7();var capture=new CaptureAiKnowledgeEvaluationStrategy(Guid.CreateVersion7(),0,dataset.Id,dataset.Revision.Id,dataset.Version,dataset.ApprovalId!.Value,
                "实际比较"+candidate,"keyword",candidate,embedding,embeddingVersion,candidate=="vector"?null:processing,candidate=="vector"?null:processingVersion,[document],search.Sources);
            await Expect(HttpMethod.Put,root+"/evaluation-strategies/"+id,reviewer,capture,HttpStatusCode.Forbidden);
            var strategy=await Send<AiKnowledgeEvaluationStrategy>(HttpMethod.Put,root+"/evaluation-strategies/"+id,owner,capture);strategies.Add(strategy);
            Assert.AreEqual(strategy.RevisionId,(await Send<AiKnowledgeEvaluationStrategy>(HttpMethod.Put,root+"/evaluation-strategies/"+id,owner,capture)).RevisionId);
            await Expect(HttpMethod.Put,root+"/evaluation-strategies/"+id,owner,capture with{Name="同键换正文"},HttpStatusCode.Conflict);
            var admission=await Send<AiKnowledgeEvaluationRunAdmission>(HttpMethod.Get,root+$"/evaluation-strategies/{id}/admission?revisionId={strategy.RevisionId}",owner);
            Assert.AreEqual(candidate=="vector"?2:10,admission.MaximumModelCalls);Assert.AreEqual(2,admission.CaseCount);
            var create=new CreateAiKnowledgeEvaluationRun(Guid.CreateVersion7(),strategy.RevisionId,admission.Digest,true);
            await Expect(HttpMethod.Post,root+$"/evaluation-strategies/{id}/runs",owner,create with{ConfirmPaidModelCalls=false},HttpStatusCode.BadRequest);
            var run=await Send<AiKnowledgeEvaluationRun>(HttpMethod.Post,root+$"/evaluation-strategies/{id}/runs",owner,create);runs.Add(run);
            Assert.AreEqual(run.Id,(await Send<AiKnowledgeEvaluationRun>(HttpMethod.Post,root+$"/evaluation-strategies/{id}/runs",owner,create)).Id);
            Assert.AreEqual(2,run.NotRun);Assert.IsNull(run.MeanDurationMs);Assert.IsNull(run.P95DurationMs);
        }
        Assert.AreEqual(baselineCalls,calls());Assert.AreEqual(ledgerBefore,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        foreach(var run in runs)
        {
            var current=run.Cases[0];var request=new ExecuteAiKnowledgeEvaluationCase(Guid.CreateVersion7(),current.Version);
            var endpoint=root+$"/evaluation-runs/{run.Id}/cases/{current.Id}";var before=calls();
            await Expect(HttpMethod.Post,endpoint+"/execute",reviewer,request,HttpStatusCode.Forbidden);
            var result=await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,endpoint+"/execute",owner,request);
            Assert.AreEqual("succeeded",result.Case.State);Assert.IsNotNull(result.Baseline);Assert.IsNotNull(result.Candidate);Assert.IsTrue(result.Case.DurationMs>0);
            var expected=run==runs[0]?1:5;Assert.AreEqual(before+expected,calls());Assert.AreEqual(expected,result.Operations!.Count);
            var actualIds=(await db.QueryAsync<Guid>("SELECT ledger.Id FROM fn_ai_operation_budget AS ledger INNER JOIN fn_ai_knowledge_evaluation_operation AS operation ON operation.Id=ledger.Id WHERE operation.CaseId=@Id",new{current.Id})).ToArray();
            CollectionAssert.AreEquivalent(actualIds,result.Operations.Select(value=>value.Id).ToArray());
            Assert.IsTrue(result.Operations.All(value=>value.UsageStatus=="known"));
            Assert.IsTrue(result.Operations.All(value=>value.PriceVersionId is not null&&value.RecordedCost is not null&&decimal.Parse(value.RecordedCost,System.Globalization.CultureInfo.InvariantCulture)>0));
            var replay=await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,endpoint+"/execute",owner,request);Assert.AreEqual(result.Case.Version,replay.Case.Version);Assert.AreEqual(before+expected,calls());
            await Expect(HttpMethod.Post,endpoint+"/execute",owner,request with{RequestKey=Guid.CreateVersion7(),ExpectedVersion=result.Case.Version},HttpStatusCode.Conflict);
            var manual=await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,endpoint+"/reviews",owner,new ReviewAiKnowledgeEvaluationResult(Guid.CreateVersion7(),result.Case.Version,true,true,true));
            Assert.AreEqual(true,manual.Case.BaselineSupportsReference);Assert.AreEqual(true,manual.Case.CandidateSupportsReference);
            var publicRead=await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Get,endpoint,reviewer);Assert.AreEqual("not_authorized",publicRead.FinanceState);Assert.IsNull(publicRead.Operations);
            await Expect(HttpMethod.Get,"/api/v1/ai/knowledge-finance",reviewer,null,HttpStatusCode.Forbidden);
            var skipped=await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,root+$"/evaluation-runs/{run.Id}/cases/{run.Cases[1].Id}/skip",owner,new ExecuteAiKnowledgeEvaluationCase(Guid.CreateVersion7(),1));
            Assert.AreEqual("skipped",skipped.Case.State);Assert.AreEqual(before+expected,calls());
            var summary=await Send<AiKnowledgeEvaluationRun>(HttpMethod.Get,root+$"/evaluation-runs/{run.Id}",owner);Assert.AreEqual(2,summary.Total);Assert.AreEqual(1,summary.Succeeded);Assert.AreEqual(1,summary.Skipped);Assert.AreEqual(1,summary.Measured);
        }
        Assert.AreEqual(ledgerBefore+6,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        var finance=await Send<AiKnowledgeFinanceReport>(HttpMethod.Get,"/api/v1/ai/knowledge-finance?knowledgeBaseId="+knowledge,owner);
        Assert.AreEqual(6,finance.Items.Sum(item=>item.Requests));Assert.IsTrue(finance.Items.All(item=>item.KnowledgeBaseId==knowledge&&item.Attribution=="explicit_operation"));
        CollectionAssert.AreEquivalent(new[]{"USD","CNY"},finance.Items.Select(item=>item.Currency).Distinct().ToArray());
        Assert.IsTrue(finance.Items.All(item=>item.PriceVersionId is not null&&item.MissingCost==0&&item.RecordedCost is not null&&decimal.Parse(item.RecordedCost,System.Globalization.CultureInfo.InvariantCulture)>0));
        var originalStrategy=strategies[0];var append=new CaptureAiKnowledgeEvaluationStrategy(Guid.CreateVersion7(),1,dataset.Id,dataset.Revision.Id,dataset.Version,dataset.ApprovalId!.Value,
            "追加不可变策略版本","keyword","vector",embedding,embeddingVersion,null,null,[document],search.Sources);
        var appended=await Send<AiKnowledgeEvaluationStrategy>(HttpMethod.Put,root+"/evaluation-strategies/"+originalStrategy.Id,owner,append);Assert.AreEqual(2,appended.Version);Assert.AreNotEqual(originalStrategy.RevisionId,appended.RevisionId);
        Assert.AreEqual(appended.RevisionId,(await Send<AiKnowledgeEvaluationStrategy>(HttpMethod.Put,root+"/evaluation-strategies/"+originalStrategy.Id,owner,append)).RevisionId);
        await Expect(HttpMethod.Put,root+"/evaluation-strategies/"+originalStrategy.Id,owner,append with{RequestKey=Guid.CreateVersion7()},HttpStatusCode.Conflict);
        Assert.AreEqual(originalStrategy.Name,(await Send<AiKnowledgeEvaluationStrategy>(HttpMethod.Get,root+$"/evaluation-strategies/{originalStrategy.Id}?revisionId={originalStrategy.RevisionId}",owner)).Name);
        var observation=await Send<AiKnowledgeQualityObservation>(HttpMethod.Get,root+"/quality-observations",owner);Assert.AreEqual("older_answers_not_recorded",observation.HistoricalCoverage);
        if(await db.QuerySingleAsync<Guid?>("SELECT TenantId FROM fn_ai_knowledge_base WHERE Id=@Id",new{Id=knowledge}) is null)
        {
            var chat=await Send<AiChatSessionResponse>(HttpMethod.Post,"/api/v1/ai/chat/sessions",owner,new CreateAiChatSessionRequest(processing,"实际结果观测"),HttpStatusCode.Created);
            var ask=new AskAiKnowledgeRequest([knowledge],embedding,sample.Question,new([document],embeddingVersion,search.Sources));
            var answered=await Send<AskAiKnowledgeResponse>(HttpMethod.Post,$"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask",owner,ask);Assert.IsFalse(answered.Refused);
            fault("answer_refusal",false);
            try{Assert.IsTrue((await Send<AskAiKnowledgeResponse>(HttpMethod.Post,$"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask",owner,ask)).Refused);}
            finally{fault(null,false);}
            var feedbackPath=$"/api/v1/ai/chat/sessions/{chat.Id}/messages/{answered.Message.Id}/feedback";
            var negative=await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Put,feedbackPath,owner,new SetAiKnowledgeAnswerFeedbackRequest("unhelpful","unsupported",[answered.Message.Citations[0].Id],0));
            var current=await Send<AiKnowledgeQualityObservation>(HttpMethod.Get,root+"/quality-observations",owner);Assert.AreEqual(2L,current.RecordedAnswers);Assert.AreEqual(1L,current.Refused);Assert.AreEqual(2L,current.Measured);Assert.IsTrue(current.MeanPipelineMs>0);Assert.AreEqual(1L,current.CurrentNegativeFeedback);
            await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Delete,feedbackPath+"?version="+negative.Version,owner);
            Assert.AreEqual(0L,(await Send<AiKnowledgeQualityObservation>(HttpMethod.Get,root+"/quality-observations",owner)).CurrentNegativeFeedback);
        }
        // 未报告用量时仍是真实模型运行，已完成排名不能把账本未知金额写成零。
        fault(null,true);
        AiKnowledgeEvaluationRun unknown;
        try
        {
            unknown=await NewRun(strategies[0]);var result=await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,root+$"/evaluation-runs/{unknown.Id}/cases/{unknown.Cases[0].Id}/execute",owner,new ExecuteAiKnowledgeEvaluationCase(Guid.CreateVersion7(),1));
            Assert.AreEqual("succeeded",result.Case.State);Assert.AreEqual("unknown",result.Operations!.Single().UsageStatus);Assert.IsNull(result.Operations!.Single().RecordedCost);
            var priced=result.Operations!.Single();Assert.AreEqual("USD",priced.Currency);Assert.IsNotNull(priced.ReservedCost);
        }
        finally{fault(null,false);}
        fault("malformed",false);
        AiKnowledgeEvaluationRun failed;
        try
        {
            failed=await NewRun(strategies[1]);var item=failed.Cases[0];var request=new ExecuteAiKnowledgeEvaluationCase(Guid.CreateVersion7(),1);
            var before=calls();var result=await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,root+$"/evaluation-runs/{failed.Id}/cases/{item.Id}/execute",owner,request);
            Assert.AreEqual("failed",result.Case.State);Assert.IsNull(result.Baseline);Assert.IsNull(result.Candidate);Assert.AreEqual(before+3,calls());
            await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,root+$"/evaluation-runs/{failed.Id}/cases/{item.Id}/execute",owner,request);Assert.AreEqual(before+3,calls());
        }
        finally{fault(null,false);}
        if(nativeHttp)
        {
            // 真正HTTP 503只能派发一次；保留前两次嵌入，共三次请求，不继承宿主自动重试。
            fault("reject",false);
            try
            {
                var rejected=await NewRun(strategies[1]);var item=rejected.Cases[0];var before=calls();
                var request=new ExecuteAiKnowledgeEvaluationCase(Guid.CreateVersion7(),1);var endpoint=root+$"/evaluation-runs/{rejected.Id}/cases/{item.Id}/execute";
                Assert.AreEqual("failed",(await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,endpoint,owner,request)).Case.State);Assert.AreEqual(before+3,calls());
                await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,endpoint,owner,request);Assert.AreEqual(before+3,calls());
            }
            finally{fault(null,false);}
        }
        var all=(await Send<AiKnowledgeEvaluationEntryPage>(HttpMethod.Get,root+"/evaluation-runs",owner)).Items;Assert.AreEqual(nativeHttp?5:4,all.Count);
        // 明确构造进程中断后的领取元数据，只证明关闭/原键重放，不声称这里中断了真实模型。
        var interrupted=await NewRun(strategies[0]);var interruptedId=interrupted.Cases[0].Id;var callsBeforeClose=calls();
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_evaluation_case SET StateKey='running',Version=2,ExecutionToken=@Token,StartedAtUtc=@Now WHERE Id=@Id AND StateKey='not_run'",new{Id=interruptedId,Token=Guid.CreateVersion7(),Now=DateTime.UtcNow});
        var close=new ExecuteAiKnowledgeEvaluationCase(Guid.CreateVersion7(),2);var closePath=root+$"/evaluation-runs/{interrupted.Id}/cases/{interruptedId}/close-unknown";
        var closed=await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,closePath,owner,close);Assert.AreEqual("unknown",closed.Case.State);Assert.IsNull(closed.Case.DurationMs);
        Assert.AreEqual(closed.Case.Version,(await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,closePath,owner,close)).Case.Version);Assert.AreEqual(callsBeforeClose,calls());
        var afterModels=calls();await replace();var afterReplacement=calls();Assert.IsTrue(afterReplacement>afterModels);
        foreach(var strategy in strategies)await Expect(HttpMethod.Get,root+$"/evaluation-strategies/{strategy.Id}",owner,null,HttpStatusCode.Conflict);
        foreach(var run in runs)
        {
            await Expect(HttpMethod.Get,root+$"/evaluation-runs/{run.Id}",owner,null,HttpStatusCode.Conflict);
            await Expect(HttpMethod.Get,root+$"/evaluation-runs/{run.Id}/cases/{run.Cases[0].Id}",reviewer,null,HttpStatusCode.Conflict);
        }
        Assert.AreEqual(afterReplacement,calls());Assert.AreEqual(files,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
        Console.WriteLine("K05 actual retrieval-pair execution, exact phase ledger, unknown usage, failure denominator and source replacement verified; not cloud quality.");
        async Task<AiKnowledgeEvaluationRun> NewRun(AiKnowledgeEvaluationStrategy strategy)
        {
            var admission=await Send<AiKnowledgeEvaluationRunAdmission>(HttpMethod.Get,root+$"/evaluation-strategies/{strategy.Id}/admission?revisionId={strategy.RevisionId}",owner);
            return await Send<AiKnowledgeEvaluationRun>(HttpMethod.Post,root+$"/evaluation-strategies/{strategy.Id}/runs",owner,new CreateAiKnowledgeEvaluationRun(Guid.CreateVersion7(),strategy.RevisionId,admission.Digest,true));
        }
        async Task<T> Send<T>(HttpMethod method,string url,string actor,object? body=null,HttpStatusCode expected=HttpStatusCode.OK)
        {using var response=await Request(method,url,actor,body);var text=await response.Content.ReadAsStringAsync();Assert.AreEqual(expected,response.StatusCode,$"{method} {url}: {text}");return JsonSerializer.Deserialize<T>(text,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;}
        async Task Expect(HttpMethod method,string url,string actor,object? body,HttpStatusCode expected)
        {using var response=await Request(method,url,actor,body);Assert.AreEqual(expected,response.StatusCode,$"{method} {url}: {await response.Content.ReadAsStringAsync()}");}
        async Task<HttpResponseMessage> Request(HttpMethod method,string url,string actor,object? body)
        {using var request=new HttpRequestMessage(method,url);request.Headers.Authorization=new("Bearer",actor);if(body is not null)request.Content=JsonContent.Create(body,body.GetType());return await client.SendAsync(request);}
    }

    internal static async Task VerifyLocalizedErrorsAsync(HttpClient client,Guid knowledge,string owner)
    {
        foreach(var language in new[]{"zh-CN","en-US"})foreach(var resource in new[]{"evaluation-sets","evaluation-strategies"})
        {
            using var invalid=new HttpRequestMessage(HttpMethod.Get,$"/api/v1/ai/knowledge-bases/{knowledge}/"+resource+"/"+Guid.Empty);invalid.Headers.Authorization=new("Bearer",owner);invalid.Headers.AcceptLanguage.ParseAdd(language);
            using var denied=await client.SendAsync(invalid);Assert.AreEqual(HttpStatusCode.BadRequest,denied.StatusCode,await denied.Content.ReadAsStringAsync());
            using var problem=JsonDocument.Parse(await denied.Content.ReadAsStringAsync());var code="ai.knowledge_evaluation_"+(resource=="evaluation-sets"?"sets":"runs")+".input_invalid";
            Assert.AreEqual(code,problem.RootElement.GetProperty("code").GetString());Assert.AreEqual("https://full.net/errors/"+code,problem.RootElement.GetProperty("type").GetString());
            Assert.AreEqual(language,denied.Content.Headers.ContentLanguage.Single());var title=problem.RootElement.GetProperty("title").GetString()!;
            Assert.AreEqual(language=="zh-CN",title.Any(value=>value is >= '\u4e00' and <= '\u9fff'));
        }
    }
}
