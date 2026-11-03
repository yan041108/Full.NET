using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>评估集以真实批准反馈和 FAQ/文档完整来源为依据，移出样例也不能洗掉私人限制。</summary>
internal static class KnowledgeEvaluationSourceAssertions
{
    internal static async Task VerifyAsync(HttpClient client, DbConnection db, Guid sourceKnowledge, AiKnowledgeFaqDraftWorkspace faq,
        Guid embedding, int embeddingVersion, Guid generation, int generationVersion, string owner, string reader, string reviewer,
        Guid[] members, Func<int> modelCalls, Func<AiKnowledgeSearchSource, Task<(Guid SessionId, Guid MessageId)>>? seedHistory,bool verifyRuns=false)
    {
        const string privateQuestion = "PRIVATE_EVALUATION_USER_QUESTION_禁止交付";
        string? privateAnswer = null;
        var search = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", reader,
            new SearchAiKnowledgeRequest([sourceKnowledge], [faq.ReservedDocumentId], embedding, embeddingVersion, "标准", 20, false, null));
        var source = search.Sources.Single();
        var ask = new AskAiKnowledgeRequest([sourceKnowledge], embedding, privateQuestion, new([source.DocumentId], embeddingVersion, [source]));
        (Guid SessionId, Guid MessageId) answer;
        if (seedHistory is null)
        {
            var chat = await Send<AiChatSessionResponse>(HttpMethod.Post, "/api/v1/ai/chat/sessions", reader,
                new CreateAiChatSessionRequest(generation, "评估集真实来源"), HttpStatusCode.Created);
            var result = await Send<AskAiKnowledgeResponse>(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask", reader, ask);
            Assert.IsFalse(result.Refused); answer = (chat.Id, result.Message.Id);
        }
        else
        {
            // 租户聊天写入尚未开放；这里只使用完整真实来源的持久历史夹具，不冒充租户模型派发。
            await Expect(HttpMethod.Post, "/api/v1/ai/chat/sessions", reader, new CreateAiChatSessionRequest(generation, "租户边界"), HttpStatusCode.Forbidden);
            answer = await seedHistory(source);
            await Expect(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{answer.SessionId}/knowledge/ask", reader, ask, HttpStatusCode.Forbidden);
        }
        var history = await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{answer.SessionId}", reader);
        var message = history.Messages.Single(value => value.Id == answer.MessageId);
        privateAnswer = message.Content;
        Assert.IsNull(message.KnowledgeUnavailableCode); Assert.IsTrue(message.Citations.Count > 0);
        var feedbackPath = $"/api/v1/ai/chat/sessions/{answer.SessionId}/messages/{answer.MessageId}/feedback";
        var feedback = await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Put, feedbackPath, reader,
            new SetAiKnowledgeAnswerFeedbackRequest("helpful", "accurate", [message.Citations[0].Id], 0));
        var review = await Send<AiKnowledgeAnswerFeedbackReviewItem>(HttpMethod.Put, $"/api/v1/ai/knowledge-answer-feedback/reviews/{feedback.Id}", owner,
            new ReviewAiKnowledgeAnswerFeedbackRequest("approved", feedback.Version, 0));
        var target = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", owner,
            new CreateAiKnowledgeBaseRequest("持久脱敏评估来源", null), HttpStatusCode.Created);
        var kb = $"/api/v1/ai/knowledge-bases/{target.Id}";
        target = await Send<AiKnowledgeBaseResponse>(HttpMethod.Put, kb + "/policy", owner,
            new UpdateAiKnowledgePolicyRequest("restricted", embedding, embeddingVersion, generation, generationVersion, target.Version));
        await Send<AiKnowledgeMembersResponse>(HttpMethod.Put, kb + "/members", owner, new SetAiKnowledgeMembersRequest(members, target.Version));
        var path = kb + "/evaluation-sets";
        var sample = new AiFeedbackEvaluationSample("zh-CN", "人工脱敏问题", "人工脱敏参考答案", "人工核验的脱敏依据", false);
        var legacy = await Send<AiFeedbackEvaluationCandidate>(HttpMethod.Post, $"/api/v1/ai/knowledge-answer-feedback/{feedback.Id}/evaluation-candidate", owner,
            new PrepareAiFeedbackEvaluationRequest(feedback.Version, review.ReviewVersion, sample));
        var noCalls = modelCalls(); var noCost = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        var files = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file");
        var prepared = await Send<AiKnowledgeEvaluationPreparedFeedback>(HttpMethod.Post, path + "/feedback-candidates", owner,
            new PrepareAiKnowledgeEvaluationFeedback(legacy.CandidateToken));
        var input = new AiKnowledgeEvaluationCaseInput(null, sample, null, prepared.PersistenceToken);
        var create = new SaveAiKnowledgeEvaluationSet(Guid.CreateVersion7(), null, 0, "人工脱敏冻结集", [input], true, true);
        await Expect(HttpMethod.Post, path, owner, create with { RequestKey = Guid.CreateVersion7(), Cases = [input with { FeedbackCandidateToken = legacy.CandidateToken }] }, HttpStatusCode.Conflict);
        var saved = await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post, path, owner, create);
        var workspace = saved.Workspace!; Assert.AreEqual("available", saved.DeliveryState);
        Assert.AreEqual(1, workspace.Revision.SourceCount); Assert.IsFalse(workspace.CanIndependentlyApprove);
        Assert.IsTrue((await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post, path, owner, create)).Replayed);
        var detail = path + "/" + workspace.Id;
        // 审核者只有评估私人窄用途，没有 FAQ 私人答案权限；直接反馈不应偷换成 FAQ 权限。
        var seen = await Send<AiKnowledgeEvaluationWorkspace>(HttpMethod.Get, detail, reviewer); Assert.IsTrue(seen.CanIndependentlyApprove);
        var approve = new ReviewAiKnowledgeEvaluationSet(Guid.CreateVersion7(), workspace.Revision.Id, workspace.Version, "approved", true);
        await Expect(HttpMethod.Post, detail + "/approvals", owner, approve, HttpStatusCode.Conflict);
        workspace = (await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post, detail + "/approvals", reviewer, approve)).Workspace!;
        Assert.AreEqual("approved", workspace.ApprovalState);
        AiKnowledgeEvaluationRun? privateRun=null;AiKnowledgeEvaluationStrategy? privateStrategy=null;
        if(verifyRuns)
        {
            // 独立保留一份准确批准修订；主场景的编辑/恢复不能提前令运行失效，掩盖后面的真实来源撤回检查。
            var retained=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,detail+"/copy",owner,new CopyAiKnowledgeEvaluationSet(Guid.CreateVersion7(),workspace.Revision.Id,workspace.Version))).Workspace!;
            retained=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Put,path+"/"+retained.Id,owner,new SaveAiKnowledgeEvaluationSet(Guid.CreateVersion7(),retained.Revision.Id,retained.Version,"实际运行保留来源",
                retained.Revision.Cases.Select(item=>new AiKnowledgeEvaluationCaseInput(item.Id,item.Sample,item.IndependentBasis,null)).ToArray(),true,true))).Workspace!;
            retained=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,path+$"/{retained.Id}/approvals",reviewer,new ReviewAiKnowledgeEvaluationSet(Guid.CreateVersion7(),retained.Revision.Id,retained.Version,"approved",true))).Workspace!;
            var strategyId=Guid.CreateVersion7();var capture=new CaptureAiKnowledgeEvaluationStrategy(Guid.CreateVersion7(),0,retained.Id,retained.Revision.Id,retained.Version,retained.ApprovalId!.Value,
                "私人来源评估与完整FAQ候选","keyword","vector",embedding,embeddingVersion,null,null,[source.DocumentId],[source]);
            privateStrategy=await Send<AiKnowledgeEvaluationStrategy>(HttpMethod.Put,kb+"/evaluation-strategies/"+strategyId,owner,capture);
            var admission=await Send<AiKnowledgeEvaluationRunAdmission>(HttpMethod.Get,kb+$"/evaluation-strategies/{strategyId}/admission?revisionId={privateStrategy.RevisionId}",owner);
            privateRun=await Send<AiKnowledgeEvaluationRun>(HttpMethod.Post,kb+$"/evaluation-strategies/{strategyId}/runs",owner,new CreateAiKnowledgeEvaluationRun(Guid.CreateVersion7(),privateStrategy.RevisionId,admission.Digest,true));
            var result=await Send<AiKnowledgeEvaluationCaseResult>(HttpMethod.Post,kb+$"/evaluation-runs/{privateRun.Id}/cases/{privateRun.Cases[0].Id}/execute",owner,new ExecuteAiKnowledgeEvaluationCase(Guid.CreateVersion7(),1));
            Assert.AreEqual("succeeded",result.Case.State);Assert.AreEqual(source.DocumentId,result.Candidate!.Hits[0].Source.DocumentId);
            Assert.AreEqual("faq-revision-v1",result.Candidate.Hits[0].Source.SourceKind);Assert.AreEqual(1,result.Operations!.Count);
            noCalls=modelCalls();noCost=await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        }
        var originalRevision = workspace.Revision.Id;
        var sourceHash = workspace.Revision.SourceHash;
        var copy = (await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post, detail + "/copy", owner,
            new CopyAiKnowledgeEvaluationSet(Guid.CreateVersion7(), workspace.Revision.Id, workspace.Version))).Workspace!;
        workspace = (await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Put, detail, owner,
            new SaveAiKnowledgeEvaluationSet(Guid.CreateVersion7(), workspace.Revision.Id, workspace.Version, "移除样例仍保留来源",
                [new(null, sample with { Question = "独立人工字段" }, "明确人工依据", null)], true, false))).Workspace!;
        Assert.AreEqual(1, workspace.Revision.SourceCount); Assert.AreEqual(sourceHash, workspace.Revision.SourceHash);
        workspace = (await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post, detail + "/restore", owner,
            new RestoreAiKnowledgeEvaluationSet(Guid.CreateVersion7(), workspace.Revision.Id, workspace.Version, originalRevision))).Workspace!;
        Assert.AreEqual(sourceHash, workspace.Revision.SourceHash); Assert.AreEqual(originalRevision, workspace.Revision.RestoredFromRevisionId);
        var footprint = await db.QuerySingleAsync<string>("SELECT FootprintJson FROM fn_ai_knowledge_evaluation_set WHERE Id=@Id", new { workspace.Id });
        using (var stored = JsonDocument.Parse(footprint))
        using (var provenance = JsonDocument.Parse(stored.RootElement.GetProperty("sources")[0].GetString()!))
        {
            Assert.AreEqual(feedback.Id, provenance.RootElement.GetProperty("feedback").GetProperty("feedbackId").GetGuid());
            Assert.AreEqual(faq.Id, provenance.RootElement.GetProperty("graph").GetProperty("nodes")[0].GetProperty("source").GetProperty("faqId").GetGuid());
            Assert.IsTrue(provenance.RootElement.GetProperty("dependencies").GetArrayLength() > 0);
        }
        var versions = await Counts();
        if(privateRun is not null)Assert.AreEqual(privateRun.Id,(await Send<AiKnowledgeEvaluationRun>(HttpMethod.Get,kb+"/evaluation-runs/"+privateRun.Id,owner)).Id);
        var originalLineage = await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id", new { Id = answer.MessageId });
        foreach (var changeAnswer in new[] { true, false })
        {
            var sql = changeAnswer ? "UPDATE fn_ai_chat_message SET Content=@Value WHERE Id=@Id" : "UPDATE fn_ai_chat_message SET KnowledgeLineage=@Value WHERE Id=@Id";
            await db.ExecuteAsync(sql, new { Value = changeAnswer ? message.Content + "原回答字节改变" : originalLineage + " \n", Id = answer.MessageId });
            try { await Rejected(); }
            finally { await db.ExecuteAsync(sql, new { Value = changeAnswer ? message.Content : originalLineage, Id = answer.MessageId }); }
            Assert.AreEqual(sourceHash, (await Send<AiKnowledgeEvaluationWorkspace>(HttpMethod.Get, detail, owner)).Revision.SourceHash);
        }
        review = await Send<AiKnowledgeAnswerFeedbackReviewItem>(HttpMethod.Put, $"/api/v1/ai/knowledge-answer-feedback/reviews/{feedback.Id}", owner,
            new ReviewAiKnowledgeAnswerFeedbackRequest("rejected", feedback.Version, review.ReviewVersion));
        await Rejected();
        review = await Send<AiKnowledgeAnswerFeedbackReviewItem>(HttpMethod.Put, $"/api/v1/ai/knowledge-answer-feedback/reviews/{feedback.Id}", owner,
            new ReviewAiKnowledgeAnswerFeedbackRequest("approved", feedback.Version, review.ReviewVersion));
        // 重新批准是新版本，旧评估来源仍不可恢复为当前资格。
        await Rejected();
        await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Delete, feedbackPath + "?version=" + feedback.Version, reader);
        await Rejected();
        Assert.AreEqual(noCalls, modelCalls()); Assert.AreEqual(noCost, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Assert.AreEqual(files, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));

        async Task Rejected()
        {
            if(privateRun is not null)
            {
                await Expect(HttpMethod.Get,kb+$"/evaluation-runs/{privateRun.Id}",owner,null,HttpStatusCode.Conflict);
                await Expect(HttpMethod.Get,kb+$"/evaluation-runs/{privateRun.Id}/cases/{privateRun.Cases[0].Id}",owner,null,HttpStatusCode.Conflict);
                await Expect(HttpMethod.Get,kb+$"/evaluation-strategies/{privateStrategy!.Id}",owner,null,HttpStatusCode.Conflict);
            }
            await Expect(HttpMethod.Get, detail, owner, null, HttpStatusCode.Conflict);
            await Expect(HttpMethod.Get, detail + "?revisionId=" + originalRevision, reviewer, null, HttpStatusCode.Conflict);
            await Expect(HttpMethod.Get, path + "/" + copy.Id, owner, null, HttpStatusCode.Conflict);
            Assert.AreEqual(0, (await Send<AiKnowledgeEvaluationPage>(HttpMethod.Get, path, owner)).Items.Count);
            await Expect(HttpMethod.Post, detail + "/copy", owner, new CopyAiKnowledgeEvaluationSet(Guid.CreateVersion7(), workspace.Revision.Id, workspace.Version), HttpStatusCode.Conflict);
            await Expect(HttpMethod.Post, detail + "/restore", owner, new RestoreAiKnowledgeEvaluationSet(Guid.CreateVersion7(), workspace.Revision.Id, workspace.Version, originalRevision), HttpStatusCode.Conflict);
            await Expect(HttpMethod.Post, detail + "/approvals", reviewer, new ReviewAiKnowledgeEvaluationSet(Guid.CreateVersion7(), workspace.Revision.Id, workspace.Version, "approved", true), HttpStatusCode.Conflict);
            await Expect(HttpMethod.Post, path, owner, create with { RequestKey = Guid.CreateVersion7() }, HttpStatusCode.Conflict);
            Assert.AreEqual(versions, await Counts());
        }
        Task<string> Counts() => db.QuerySingleAsync<string>("SELECT CONCAT((SELECT COUNT(*) FROM fn_ai_knowledge_evaluation_revision),':',(SELECT COUNT(*) FROM fn_ai_knowledge_evaluation_set),':',(SELECT COUNT(*) FROM fn_ai_knowledge_evaluation_approval))");
        async Task<T> Send<T>(HttpMethod method, string url, string actor, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var response = await Request(method, url, actor, body); var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expected, response.StatusCode, $"{method} {url}: {text}");
            foreach (var forbidden in new[] { privateQuestion, privateAnswer ?? "__unseen_private_answer__", "ownerUserId", "sessionId", "knowledgeLineage" })
                if (forbidden.Length > 0 && (url.Contains("evaluation-sets", StringComparison.Ordinal)||url.Contains("evaluation-runs",StringComparison.Ordinal)||url.Contains("evaluation-strategies",StringComparison.Ordinal))) Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal));
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        async Task Expect(HttpMethod method, string url, string actor, object? body, HttpStatusCode expected)
        { using var response = await Request(method, url, actor, body); Assert.AreEqual(expected, response.StatusCode, $"{method} {url}: {await response.Content.ReadAsStringAsync()}"); }
        async Task<HttpResponseMessage> Request(HttpMethod method, string url, string actor, object? body)
        {
            using var request = new HttpRequestMessage(method, url); request.Headers.Authorization = new("Bearer", actor);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType()); return await client.SendAsync(request);
        }
    }
}
