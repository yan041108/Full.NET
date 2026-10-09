using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.IntegrationTests.Ai;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Native HTTP 审核及统计；历史所属人由夹具种入，不宣称这些用户登录或云模型生成。</summary>
internal static class NativeFeedbackReviewAssertions
{
    internal static async Task VerifyAsync(DbConnection db, Func<HttpMethod, string, HttpContent?, Task<HttpResponseMessage>> send,
        Guid model, AiKnowledgeAnswerLineage original, AiKnowledgeAnswerFeedbackResponse ownFeedback)
    {
        const string path = "/api/v1/ai/knowledge-answer-feedback";
        using (var emptyId = await send(HttpMethod.Put, path + $"/reviews/{Guid.Empty}", JsonContent.Create(new ReviewAiKnowledgeAnswerFeedbackRequest("approved", ownFeedback.Version, 0))))
            await AiKnowledgeAnswerFeedbackAssertions.Expect(emptyId, HttpStatusCode.BadRequest);
        using (var self = await send(HttpMethod.Put, path + $"/reviews/{ownFeedback.Id}", JsonContent.Create(new ReviewAiKnowledgeAnswerFeedbackRequest("approved", ownFeedback.Version, 0))))
            await AiKnowledgeAnswerFeedbackAssertions.Expect(self, HttpStatusCode.NotFound);
        var sessions = new List<Guid>(); var feedbackIds = new List<Guid>(); var owners = new List<Guid>();
        try
        {
            for (var i = 0; i < 31; i++)
            {
                var session = Guid.CreateVersion7(); var message = Guid.CreateVersion7(); var owner = Guid.CreateVersion7(); var id = Guid.CreateVersion7();
                sessions.Add(session); owners.Add(owner); if (i < 5) feedbackIds.Add(id);
                var json = i < 5 ? AiKnowledgeAnswerLineageCodec.Encode(original with { MessageId = message, SessionId = session, ActorUserId = owner }) : "{}";
                var p = new { Session = session, Message = message, Owner = owner, Id = id, Model = model, Json = json, Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))), Now = DateTimeOffset.UtcNow };
                await db.ExecuteAsync("INSERT INTO fn_ai_chat_session (Id,TenantId,OwnerUserId,ModelConfigId,ModelName,Title,CreatedAtUtc) VALUES (@Session,NULL,@Owner,@Model,'fixture','review fixture',@Now)", p);
                await db.ExecuteAsync("INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,IsKnowledgeAnswer,KnowledgeLineage,CreatedAtUtc) VALUES (@Message,@Session,'assistant','不可导出的夹具正文','completed',1,@Json,@Now)", p);
                await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_answer_feedback (Id,TenantId,SessionId,MessageId,ActorUserId,RatingKey,ReasonKey,CitationIdsJson,LineageHash,StatusKey,Version,CreatedAtUtc,UpdatedAtUtc) VALUES (@Id,NULL,@Session,@Message,@Owner,'helpful','accurate','[]',@Hash,'pending_review',1,@Now,@Now)", p);
            }
            var found = new List<Guid>(); string? after = null; var pages = 0;
            do
            {
                using var response = await send(HttpMethod.Get, path + "/reviews" + (after is null ? "" : "?after=" + Uri.EscapeDataString(after)), null);
                await AiKnowledgeAnswerFeedbackAssertions.Expect(response, HttpStatusCode.OK);
                var page = await response.Content.ReadFromJsonAsync<AiKnowledgeAnswerFeedbackReviewPage>(); Assert.IsNotNull(page);
                Assert.IsTrue(page.Items.Count <= 25); found.AddRange(page.Items.Select(item => item.FeedbackId)); after = page.NextCursor; pages++;
                if (after is not null) Assert.IsFalse(Guid.TryParse(after, out _));
                Assert.IsTrue(pages <= 3);
            } while (after is not null);
            CollectionAssert.AreEquivalent(feedbackIds.ToArray(), found.ToArray()); Assert.AreEqual(2, pages);
            using (var forgedCursor = await send(HttpMethod.Get, path + "/reviews?after=" + feedbackIds[0], null)) await AiKnowledgeAnswerFeedbackAssertions.Expect(forgedCursor, HttpStatusCode.BadRequest);
            for (var i = 0; i < 5; i++)
            {
                var accepted = await Decide(feedbackIds[i], "approved", 0); Assert.AreEqual(1, accepted.ReviewVersion);
                if (i == 3) Assert.HasCount(0, (await Export()).Cells);
            }
            var replay = await Decide(feedbackIds[0], "approved", 0); Assert.AreEqual(1, replay.ReviewVersion);
            var exported = await Export(); Assert.IsTrue(exported.IsPartial); Assert.AreEqual("feedback-labels-v1", exported.PolicyVersion);
            Assert.AreEqual(new AiKnowledgeAnswerFeedbackExportCell("helpful", "accurate", 0, 5), exported.Cells.Single());
            using (var response = await send(HttpMethod.Get, path + "/export", null))
            {
                await AiKnowledgeAnswerFeedbackAssertions.Expect(response, HttpStatusCode.OK); var json = await response.Content.ReadAsStringAsync();
                Assert.IsFalse(json.Contains("不可导出", StringComparison.Ordinal)); foreach (var owner in owners) Assert.IsFalse(json.Contains(owner.ToString(), StringComparison.OrdinalIgnoreCase));
                foreach (var id in feedbackIds) Assert.IsFalse(json.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));
                var cell = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("cells")[0];
                CollectionAssert.AreEquivalent(new[] { "ratingKey", "reasonKey", "citationCount", "feedbackCount" }, cell.EnumerateObject().Select(property => property.Name).ToArray());
            }
            Assert.AreEqual(2, (await Decide(feedbackIds[0], "rejected", 1)).ReviewVersion); Assert.HasCount(0, (await Export()).Cells);
            using (var stale = await send(HttpMethod.Put, path + $"/reviews/{feedbackIds[0]}", JsonContent.Create(new ReviewAiKnowledgeAnswerFeedbackRequest("approved", 1, 0)))) await AiKnowledgeAnswerFeedbackAssertions.Expect(stale, HttpStatusCode.Conflict);
            Assert.AreEqual(3, (await Decide(feedbackIds[0], "approved", 2)).ReviewVersion);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_answer_feedback SET ReasonKey='useful',Version=Version+1 WHERE Id=@Id", new { Id = feedbackIds[0] });
            Assert.HasCount(0, (await Export()).Cells);
            using (var changed = await send(HttpMethod.Put, path + $"/reviews/{feedbackIds[0]}", JsonContent.Create(new ReviewAiKnowledgeAnswerFeedbackRequest("approved", 1, 2)))) await AiKnowledgeAnswerFeedbackAssertions.Expect(changed, HttpStatusCode.Conflict);
            // 同一来源被撤销时，批准标签也不得继续导出。
            var document = original.Dependencies[0].DocumentId;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@Id", new { Id = document });
            try { Assert.HasCount(0, (await Export()).Cells); }
            finally { await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=0 WHERE Id=@Id", new { Id = document }); }
        }
        finally
        {
            // 会话到消息没有级联外键；与生产删除流程一致，先删除消息并验证反馈到审核的真实级联。
            foreach (var session in sessions)
            {
                await db.ExecuteAsync("DELETE FROM fn_ai_chat_message WHERE SessionId=@Id", new { Id = session });
                await db.ExecuteAsync("DELETE FROM fn_ai_chat_session WHERE Id=@Id", new { Id = session });
            }
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_answer_review WHERE FeedbackId IN @Ids", new { Ids = feedbackIds.ToArray() }));
        }
        async Task<AiKnowledgeAnswerFeedbackReviewItem> Decide(Guid id, string decision, int version)
        {
            using var response = await send(HttpMethod.Put, path + $"/reviews/{id}", JsonContent.Create(new ReviewAiKnowledgeAnswerFeedbackRequest(decision, 1, version)));
            await AiKnowledgeAnswerFeedbackAssertions.Expect(response, HttpStatusCode.OK); var value = await response.Content.ReadFromJsonAsync<AiKnowledgeAnswerFeedbackReviewItem>(); Assert.IsNotNull(value); return value;
        }
        async Task<AiKnowledgeAnswerFeedbackExportResponse> Export()
        {
            using var response = await send(HttpMethod.Get, path + "/export", null); await AiKnowledgeAnswerFeedbackAssertions.Expect(response, HttpStatusCode.OK);
            var value = await response.Content.ReadFromJsonAsync<AiKnowledgeAnswerFeedbackExportResponse>(); Assert.IsNotNull(value); return value;
        }
    }
}
