using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实私人混合答案的完整来源不得经标签审核、FAQ或脱敏候选升级为跨用户权限。</summary>
internal static class PersonalScheduleFeedbackAssertions
{
    internal static async Task VerifyAsync(HttpClient client, DbConnection db, string administrator, string owner,
        Guid knowledge, Guid embedding, int embeddingVersion, Guid generation, int generationVersion,
        Func<IReadOnlyList<Guid>?, Task<(Guid SessionId, Guid MessageId)>>? seedTenantHistory)
    {
        const string calendar = "/api/v1/calendar/my-personal-schedules";
        const string privateQuestion = "PRIVATE_K06_FEEDBACK_仅作拒绝核证";
        var now = DateTimeOffset.UtcNow;
        var schedules = new List<PersonalScheduleResponse>();
        foreach (var text in new[] { "业务反馈已选日程", "业务反馈未引用但完整保留的日程" })
            schedules.Add(await Send<PersonalScheduleResponse>(HttpMethod.Post, calendar, owner,
                new CreatePersonalScheduleRequest(text, now, now), HttpStatusCode.Created));
        var preview = await Send<AiPersonalScheduleSourcePreview>(HttpMethod.Post,
            "/api/v1/ai/personal-schedule-assistant/sources/preview", owner,
            new PreviewAiPersonalScheduleSources(schedules.Select(value => value.Id).ToArray()));
        var ask = new AskAiKnowledgeRequest([knowledge], embedding, privateQuestion,
            PersonalSchedules: preview.Sources.Select(value => new AiPersonalScheduleSourceSelection(value.Id, value.Version, value.ProjectionHash)).ToArray());
        var beforeAnswer = await Count("fn_ai_operation_budget");
        (Guid SessionId, Guid MessageId) saved;
        if (seedTenantHistory is null)
        {
            var chat = await Send<AiChatSessionResponse>(HttpMethod.Post, "/api/v1/ai/chat/sessions", owner,
                new CreateAiChatSessionRequest(generation, "完整业务反馈来源"), HttpStatusCode.Created);
            var answer = await Send<AskAiKnowledgeResponse>(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask", owner, ask);
            Assert.IsFalse(answer.Refused); saved = (chat.Id, answer.Message.Id);
            Assert.AreEqual(beforeAnswer + 2, await Count("fn_ai_operation_budget"), "只有真实问答的向量和答案阶段计费。");
        }
        else
        {
            await Exact(HttpMethod.Post, "/api/v1/ai/chat/sessions", owner, new CreateAiChatSessionRequest(generation, "租户未开放写入"), HttpStatusCode.Forbidden);
            saved = await seedTenantHistory(schedules.Select(value => value.Id).ToArray());
            await Exact(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{saved.SessionId}/knowledge/ask", owner, ask, HttpStatusCode.Forbidden);
            Assert.AreEqual(beforeAnswer, await Count("fn_ai_operation_budget"), "租户只核证真实来源历史，不伪装模型派发。");
        }
        var sessionPath = $"/api/v1/ai/chat/sessions/{saved.SessionId}";
        var history = await Send<AiChatSessionResponse>(HttpMethod.Get, sessionPath, owner);
        var message = history.Messages.Single(value => value.Id == saved.MessageId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(message.Content)); Assert.IsNull(message.KnowledgeUnavailableCode);
        Assert.IsTrue(message.Citations.Count > 0); Assert.AreEqual(0, message.PersonalScheduleCitations.Count,
            "本场景故意只引用文档；两条未引用日程仍须约束反馈和派生资格。");
        var json = await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id", new { Id = saved.MessageId });
        using (var lineage = JsonDocument.Parse(json))
        {
            Assert.AreEqual(8, lineage.RootElement.GetProperty("version").GetInt32());
            var ids = lineage.RootElement.GetProperty("personalSchedules").EnumerateArray().Select(value => value.GetProperty("id").GetGuid()).ToArray();
            CollectionAssert.AreEquivalent(schedules.Select(value => value.Id).ToArray(), ids);
        }
        var feedbackPath = sessionPath + $"/messages/{saved.MessageId}/feedback";
        var feedback = await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Put, feedbackPath, owner,
            new SetAiKnowledgeAnswerFeedbackRequest("helpful", "accurate", [message.Citations[0].Id], 0));
        // 同一主体、库和精确权限的普通文档对照必须成功，避免把所有审核均不可用误判为业务来源隔离。
        (Guid SessionId, Guid MessageId) control;
        if (seedTenantHistory is null)
        {
            var chat = await Send<AiChatSessionResponse>(HttpMethod.Post, "/api/v1/ai/chat/sessions", owner,
                new CreateAiChatSessionRequest(generation, "文档来源对照"), HttpStatusCode.Created);
            var answer = await Send<AskAiKnowledgeResponse>(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask", owner,
                ask with { Question = "仅说明文档人工核对事实", PersonalSchedules = null });
            Assert.IsFalse(answer.Refused); control = (chat.Id, answer.Message.Id);
        }
        else control = await seedTenantHistory(null);
        var controlHistory = await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{control.SessionId}", owner);
        var controlMessage = controlHistory.Messages.Single(value => value.Id == control.MessageId);
        var controlFeedback = await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Put,
            $"/api/v1/ai/chat/sessions/{control.SessionId}/messages/{control.MessageId}/feedback", owner,
            new SetAiKnowledgeAnswerFeedbackRequest("helpful", "accurate", [controlMessage.Citations[0].Id], 0));
        var controlReview = await Send<AiKnowledgeAnswerFeedbackReviewItem>(HttpMethod.Put,
            $"/api/v1/ai/knowledge-answer-feedback/reviews/{controlFeedback.Id}", administrator,
            new ReviewAiKnowledgeAnswerFeedbackRequest("approved", controlFeedback.Version, 0));
        Assert.AreEqual("approved", controlReview.ReviewStateKey);
        _ = await Send<AiFeedbackEvaluationCandidate>(HttpMethod.Post,
            $"/api/v1/ai/knowledge-answer-feedback/{controlFeedback.Id}/evaluation-candidate", administrator,
            new PrepareAiFeedbackEvaluationRequest(controlFeedback.Version, controlReview.ReviewVersion,
                new("zh-CN", "文档对照人工问题", "文档对照人工答案", "文档对照人工依据", false)));
        var target = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", administrator,
            new CreateAiKnowledgeBaseRequest("业务来源拒绝核证目标", null), HttpStatusCode.Created);
        target = await Send<AiKnowledgeBaseResponse>(HttpMethod.Put, $"/api/v1/ai/knowledge-bases/{target.Id}/policy", administrator,
            new UpdateAiKnowledgePolicyRequest("restricted", embedding, embeddingVersion, generation, generationVersion, target.Version));
        var noCost = await Count("fn_ai_operation_budget"); var noFiles = await Count("fn_files_file");
        var noFaq = await Count("fn_ai_knowledge_faq_revision"); var noSets = await Count("fn_ai_knowledge_evaluation_revision");
        var noReviews = await Count("fn_ai_knowledge_answer_review");
        var reviews = await Send<AiKnowledgeAnswerFeedbackReviewPage>(HttpMethod.Get, "/api/v1/ai/knowledge-answer-feedback/reviews", administrator);
        Assert.IsTrue(reviews.Items.Any(value => value.FeedbackId == controlFeedback.Id));
        Assert.IsFalse(reviews.Items.Any(value => value.FeedbackId == feedback.Id));
        var visible = JsonSerializer.Serialize(reviews);
        Assert.IsFalse(visible.Contains(privateQuestion, StringComparison.Ordinal));
        Assert.IsFalse(visible.Contains(message.Content, StringComparison.Ordinal));
        // 管理员持有完整精确权限，拒绝必须来自本人对象/完整来源，而不是界面隐藏或缺少Review权限。
        await Denied(HttpMethod.Put, $"/api/v1/ai/knowledge-answer-feedback/reviews/{feedback.Id}", administrator,
            new ReviewAiKnowledgeAnswerFeedbackRequest("approved", feedback.Version, 0));
        await Denied(HttpMethod.Put, $"/api/v1/ai/knowledge-answer-feedback/reviews/{feedback.Id}", owner,
            new ReviewAiKnowledgeAnswerFeedbackRequest("approved", feedback.Version, 0));
        var drafts = $"/api/v1/ai/knowledge-bases/{target.Id}/faq-drafts";
        await Denied(HttpMethod.Post, drafts + "/feedback-preview", administrator, new PreviewAiKnowledgeFaqFeedback(feedback.Id));
        await Denied(HttpMethod.Post, drafts + "/from-feedback", administrator,
            new CreateAiKnowledgeFaqFeedbackDraft(Guid.CreateVersion7(), feedback.Id, new('A', 64),
                new("人工重新措辞也不授予日程权限", [], "私人业务来源", null, null)));
        await Denied(HttpMethod.Post, $"/api/v1/ai/knowledge-answer-feedback/{feedback.Id}/evaluation-candidate", administrator,
            new PrepareAiFeedbackEvaluationRequest(feedback.Version, 1,
                new("zh-CN", "人工脱敏问题", "人工脱敏答案", "人工脱敏说明不能清空原业务来源", false)));
        Assert.AreEqual(noReviews, await Count("fn_ai_knowledge_answer_review"));
        Assert.AreEqual(noFaq, await Count("fn_ai_knowledge_faq_revision")); Assert.AreEqual(noSets, await Count("fn_ai_knowledge_evaluation_revision"));
        Assert.AreEqual(noCost, await Count("fn_ai_operation_budget")); Assert.AreEqual(noFiles, await Count("fn_files_file"));
        // 来源失效也不应剥夺本人撤回标签的窄用途；只返回状态，不能释放旧回答。
        var uncited = schedules[1];
        await Send<PersonalScheduleResponse>(HttpMethod.Put, calendar + "/" + uncited.Id, owner,
            new UpdatePersonalScheduleRequest("未引用来源已改变", now, now, uncited.Version));
        var withdrawn = await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Delete, feedbackPath + "?version=" + feedback.Version, owner);
        Assert.AreEqual("withdrawn", withdrawn.StatusKey);
        history = await Send<AiChatSessionResponse>(HttpMethod.Get, sessionPath, owner);
        Assert.AreEqual("", history.Messages.Single(value => value.Id == saved.MessageId).Content);
        Assert.AreEqual(noCost, await Count("fn_ai_operation_budget")); Assert.AreEqual(noFaq, await Count("fn_ai_knowledge_faq_revision"));
        Assert.AreEqual(noSets, await Count("fn_ai_knowledge_evaluation_revision"));

        Task<int> Count(string table) => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM " + table);
        async Task<T> Send<T>(HttpMethod method, string path, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var request = Request(method, path, token, body); using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, method + " " + path + " " + text);
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        async Task Exact(HttpMethod method, string path, string token, object body, HttpStatusCode expected)
        { using var request = Request(method, path, token, body); using var response = await client.SendAsync(request); Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync()); }
        async Task Denied(HttpMethod method, string path, string token, object body)
        {
            using var request = Request(method, path, token, body); using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync(); Assert.IsTrue(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict,
                method + " " + path + " " + text);
            Assert.IsFalse(text.Contains(privateQuestion, StringComparison.Ordinal)); Assert.IsFalse(text.Contains(message.Content, StringComparison.Ordinal));
        }
        static HttpRequestMessage Request(HttpMethod method, string path, string token, object? body)
        { var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new("Bearer", token); if (body is not null) request.Content = JsonContent.Create(body); return request; }
    }
}
