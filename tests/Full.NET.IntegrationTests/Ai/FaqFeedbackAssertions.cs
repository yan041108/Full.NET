using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实批准反馈是不可降级的私人来源；不以展示引用或客户端答案代替服务器完整谱系。</summary>
internal static class FaqFeedbackAssertions
{
    internal static async Task VerifyAsync(HttpClient client, DbConnection db, string connection, Guid sourceKnowledge, Guid leaf,
        AiKnowledgeFaqDraftWorkspace first, Guid embedding, int embeddingVersion, Guid generation, int generationVersion,
        string owner, string reader, string labelOnly, Guid[] members, Func<int> modelCalls, Func<AiKnowledgeFaqDraftWorkspace, Task<AiKnowledgeFaqPublication>> publish,
        Func<AiKnowledgeSearchSource, Task<(Guid SessionId, Guid MessageId)>>? seedHistory,
        Func<AiKnowledgeAnswerFeedbackReviewItem, Task<(string Owner, string Reader, string LabelOnly)>>? browserReady, Func<Task>? browserRevoked,
        Func<Task<(string Owner, string Reader, string LabelOnly)>> renewActors)
    {
        const string privateQuestion = "PRIVATE_USER_QUESTION_不得交付标准问题";
        if (db is MySqlConnection) StringAssert.Contains(await db.QuerySingleAsync<string>("SELECT @@optimizer_switch"), "semijoin=on");
        var originalSource = (await Search(sourceKnowledge, first.ReservedDocumentId)).Sources.Single();
        var original = await Answer(originalSource, privateQuestion);
        var originalAnswer = await History(original);
        Assert.IsNull(originalAnswer.KnowledgeUnavailableCode); Assert.IsTrue(originalAnswer.Citations.Count > 0);
        var feedbackPath = $"/api/v1/ai/chat/sessions/{original.SessionId}/messages/{original.MessageId}/feedback";
        var feedback = await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Put, feedbackPath, reader,
            new SetAiKnowledgeAnswerFeedbackRequest("helpful", "accurate", [originalAnswer.Citations[0].Id], 0));
        var target = await Target(owner, "私人反馈权威来源", members);
        var limitedTarget = await Target(labelOnly, "只有标签权限的私人目标", []);
        var drafts = Drafts(target.Id);
        await Expect(HttpMethod.Post, drafts + "/feedback-preview", owner, new PreviewAiKnowledgeFaqFeedback(feedback.Id), HttpStatusCode.Conflict);
        var review = await Review("approved", feedback.Version, 0);
        var page = await Send<AiKnowledgeAnswerFeedbackReviewPage>(HttpMethod.Get, "/api/v1/ai/knowledge-answer-feedback/reviews", labelOnly);
        Assert.IsTrue(page.Items.Any(row => row.FeedbackId == feedback.Id && row.ReviewStateKey == "approved"));
        Assert.IsFalse(JsonSerializer.Serialize(page).Contains(privateQuestion, StringComparison.Ordinal));
        await Expect(HttpMethod.Post, Drafts(limitedTarget.Id) + "/feedback-preview", labelOnly, new PreviewAiKnowledgeFaqFeedback(feedback.Id), HttpStatusCode.Forbidden);
        var noCost = await Count(); var noCalls = modelCalls(); var files = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file");
        var preview = await Send<AiKnowledgeFaqFeedbackPreview>(HttpMethod.Post, drafts + "/feedback-preview", owner, new PreviewAiKnowledgeFaqFeedback(feedback.Id));
        Assert.AreEqual(originalAnswer.Content.Trim(), preview.Answer); Assert.AreEqual(review.ReviewVersion, preview.Feedback.ReviewVersion);
        Assert.AreEqual(feedback.Version, preview.Feedback.FeedbackVersion); Assert.AreEqual(original.MessageId, preview.Feedback.MessageId);
        Assert.AreEqual(first.Id, preview.Graph!.Nodes.Single().Source.FaqId);
        Assert.AreEqual(leaf, preview.Sources.Single(s => s.SourceKind == "document-v1").DocumentId);
        Assert.AreEqual(2, preview.Sources.Count); Assert.AreEqual(noCost, await Count()); Assert.AreEqual(noCalls, modelCalls());
        var visible = JsonSerializer.Serialize(preview, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var denied in new[] { privateQuestion, "ownerUserId", "sessionId" }) Assert.IsFalse(visible.Contains(denied, StringComparison.Ordinal));
        var content = new AiKnowledgeFaqFeedbackDraftInput("人工填写的私人反馈标准问题", [], "私人反馈", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        var create = new CreateAiKnowledgeFaqFeedbackDraft(Guid.CreateVersion7(), feedback.Id, preview.Digest, content);
        var saved = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, drafts + "/from-feedback", owner, create);
        var draft = saved.Workspace;
        Assert.AreEqual("feedback_derived", draft.Origin); Assert.AreEqual(preview.SourceHash, draft.SourceHash); Assert.AreEqual(preview.Answer, draft.Revision.Content.Answer);
        Assert.AreEqual(preview.Feedback, draft.Feedback); Assert.AreEqual(1, draft.HeadVersion); Assert.AreEqual(noCost, await Count());
        Assert.AreEqual(files, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_version WHERE DocumentId=@Id", new { Id = draft.ReservedDocumentId }));
        var stored = await db.QuerySingleAsync<string>("SELECT SourceJson FROM fn_ai_knowledge_faq WHERE Id=@Id", new { Id = draft.Id });
        using (var json = JsonDocument.Parse(stored))
        {
            Assert.AreEqual(3, json.RootElement.GetProperty("version").GetInt32()); Assert.IsTrue(json.RootElement.GetProperty("feedbackPermission").GetBoolean());
            Assert.AreEqual(original.SessionId, json.RootElement.GetProperty("feedback").GetProperty("sessionId").GetGuid());
        }
        // 公共输入没有答案字段；额外字段必须在解析边界拒绝，不能悄悄忽略后落库。
        var forged = JsonSerializer.SerializeToNode(create, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        forged["content"]!["answer"] = "客户端伪造答案";
        await Expect(HttpMethod.Post, drafts + "/from-feedback", owner, forged, HttpStatusCode.BadRequest);
        draft = (await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Put, drafts + "/" + draft.Id, owner,
            new SaveAiKnowledgeFaqDraft(Guid.CreateVersion7(), draft.Revision.Id, draft.HeadVersion, draft.Revision.Content with { Answer = "人工修订仍保留私人原始来源。" }))).Workspace;
        draft = (await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, drafts + $"/{draft.Id}/restore", owner,
            new RestoreAiKnowledgeFaqDraft(Guid.CreateVersion7(), draft.Revision.Id, draft.HeadVersion, saved.Workspace.Revision.Id))).Workspace;
        var replay = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, drafts + "/from-feedback", owner, create);
        Assert.IsTrue(replay.Replayed); Assert.AreEqual(saved.SavedRevisionId, replay.SavedRevisionId); Assert.AreEqual(draft.Revision.Id, replay.Workspace.Revision.Id);
        Assert.AreEqual(stored, await db.QuerySingleAsync<string>("SELECT SourceJson FROM fn_ai_knowledge_faq WHERE Id=@Id", new { Id = draft.Id }));
        var copy = (await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, drafts + $"/{draft.Id}/copy", owner,
            new CopyAiKnowledgeFaqDraft(Guid.CreateVersion7(), draft.Revision.Id, draft.HeadVersion))).Workspace;
        Assert.AreEqual(draft.SourceHash, copy.SourceHash); Assert.AreEqual(draft.Feedback, copy.Feedback);
        Assert.AreEqual(noCost, await Count()); Assert.AreEqual(noCalls, modelCalls());
        var proposal = await publish(draft);
        var published = await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get, Published(target.Id, draft.Id), reader);
        Assert.AreEqual(draft.Feedback, published.Feedback); Assert.AreEqual(proposal.Id, published.PublicationId);
        await Expect(HttpMethod.Get, Published(target.Id, draft.Id), labelOnly, null, HttpStatusCode.Conflict);
        Assert.AreEqual(first.Id, (await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get, Published(sourceKnowledge, first.Id), labelOnly)).FaqId);
        await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", labelOnly,
            new SearchAiKnowledgeRequest([target.Id], [draft.ReservedDocumentId], embedding, embeddingVersion, "标准", 20, false, null), HttpStatusCode.Conflict);
        var publicControl = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", labelOnly,
            new SearchAiKnowledgeRequest([sourceKnowledge], [first.ReservedDocumentId], embedding, embeddingVersion, "标准", 20, false, null));
        Assert.AreEqual(first.Id, publicControl.Sources.Single().FaqId);
        var privateSource = (await Search(target.Id, draft.ReservedDocumentId)).Sources.Single();
        var directHistory = await Answer(privateSource, "私人反馈标准问题"); await PrivateHistory(directHistory, true);
        var child = (await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, drafts + "/from-sources", owner,
            new CreateAiKnowledgeFaqDerivedDraft(Guid.CreateVersion7(), draft.Revision.Content with { Question = "私人反馈父节点的后代标准问题", Aliases = [] },
                new(embedding, embeddingVersion, [privateSource])))).Workspace;
        Assert.AreEqual("faq_derived", child.Origin); Assert.IsNull(child.Feedback); Assert.AreEqual(2, child.Graph!.Nodes.Count);
        Assert.AreEqual(draft.Id, child.Graph.Nodes.Single(n => n.Source.DocumentId == draft.ReservedDocumentId).Source.FaqId);
        await Expect(HttpMethod.Post, Drafts(sourceKnowledge) + "/from-sources", owner,
            new CreateAiKnowledgeFaqDerivedDraft(Guid.CreateVersion7(), child.Revision.Content, new(embedding, embeddingVersion, [privateSource])), HttpStatusCode.Conflict);
        _ = await publish(child);
        var childSource = (await Search(target.Id, child.ReservedDocumentId)).Sources.Single();
        var scope = new AiKnowledgeSearchAnswerScope([childSource.DocumentId], embeddingVersion, [childSource]);
        var compared = await Send<AiKnowledgeSearchComparisonResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search/compare", reader,
            new CompareAiKnowledgeSearchRequest([target.Id], embedding, "标准", 20, false, scope));
        Assert.AreEqual(childSource, compared.Sources.Single());
        var refined = await Send<AiKnowledgeSearchRefinementResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search/refine", reader,
            new RefineAiKnowledgeSearchRequest([target.Id], embedding, generation, generationVersion, "标准", "rewrite_rerank", 8, false, scope));
        Assert.AreEqual(childSource, refined.Sources.Single()); Assert.AreEqual(4, refined.Operations.Count);
        var childHistory = await Answer(childSource, "标准"); await PrivateHistory(childHistory, false);
        noCalls = modelCalls();
        // 保留合法答案谱系，仅改变原回答或其原始 JSON 字节；私有派生必须核对精确原消息摘要。
        var originalLineage = await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id", new { Id = original.MessageId });
        foreach (var changeAnswer in new[] { true, false })
        {
            await db.ExecuteAsync(changeAnswer ? "UPDATE fn_ai_chat_message SET Content=@Value WHERE Id=@Id" : "UPDATE fn_ai_chat_message SET KnowledgeLineage=@Value WHERE Id=@Id",
                new { Value = changeAnswer ? originalAnswer.Content + "😀原消息改变" : originalLineage + " \n", Id = original.MessageId });
            try
            {
                await Expect(HttpMethod.Get, drafts + "/" + draft.Id, owner, null, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Get, Published(target.Id, child.Id), reader, null, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", reader,
                    new SearchAiKnowledgeRequest([target.Id], [childSource.DocumentId], embedding, embeddingVersion, "标准", 20, false, [childSource]), HttpStatusCode.Conflict);
                foreach (var identity in new[] { directHistory, childHistory })
                {
                    var hidden = await History(identity); Assert.IsNotNull(hidden.KnowledgeUnavailableCode); Assert.AreEqual("", hidden.Content); Assert.AreEqual(0, hidden.Citations.Count);
                }
            }
            finally
            {
                await db.ExecuteAsync(changeAnswer ? "UPDATE fn_ai_chat_message SET Content=@Value WHERE Id=@Id" : "UPDATE fn_ai_chat_message SET KnowledgeLineage=@Value WHERE Id=@Id",
                    new { Value = changeAnswer ? originalAnswer.Content : originalLineage, Id = original.MessageId });
            }
            Assert.IsNull((await History(childHistory)).KnowledgeUnavailableCode);
        }
        var childAnswer = await History(childHistory);
        var childFeedback = await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Put,
            $"/api/v1/ai/chat/sessions/{childHistory.SessionId}/messages/{childHistory.MessageId}/feedback", reader,
            new SetAiKnowledgeAnswerFeedbackRequest("helpful", "useful", [childAnswer.Citations[0].Id], 0));
        _ = await Send<AiKnowledgeAnswerFeedbackReviewItem>(HttpMethod.Put, $"/api/v1/ai/knowledge-answer-feedback/reviews/{childFeedback.Id}", owner,
            new ReviewAiKnowledgeAnswerFeedbackRequest("approved", childFeedback.Version, 0));
        var childPreview = await Send<AiKnowledgeFaqFeedbackPreview>(HttpMethod.Post, drafts + "/feedback-preview", owner, new PreviewAiKnowledgeFaqFeedback(childFeedback.Id));
        Assert.AreEqual(3, childPreview.Graph!.Nodes.Count); Assert.AreEqual(4, childPreview.Sources.Count);
        var nextFeedback = (await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, drafts + "/from-feedback", owner,
            new CreateAiKnowledgeFaqFeedbackDraft(Guid.CreateVersion7(), childFeedback.Id, childPreview.Digest, content with { Question = "保留私人祖先的再次反馈标准问题" }))).Workspace;
        Assert.AreEqual(3, nextFeedback.Graph!.Nodes.Count); Assert.AreEqual(childFeedback.Id, nextFeedback.Feedback!.FeedbackId);
        var transfer = $"/api/v1/ai/knowledge-bases/{target.Id}/faq-transfer";
        noCost = await Count();
        var package = await Send<AiKnowledgeFaqTransferPackage>(HttpMethod.Post, transfer + "/export", owner, new ExportAiKnowledgeFaqs([draft.Id, child.Id]));
        Assert.AreEqual("faq-transfer-v4", package.Format); Assert.AreEqual(draft.Feedback, package.Items[0].Feedback);
        var transferPreview = await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post, transfer + "/preview", owner, new PreviewAiKnowledgeFaqImport(package));
        Assert.IsTrue(transferPreview.Items.All(row => row.Status == "ready"));
        var imported = await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post, transfer + "/import", owner,
            new ImportAiKnowledgeFaqs(package, transferPreview.Digest, [Guid.CreateVersion7(), Guid.CreateVersion7()]));
        Assert.IsTrue(imported.Items.All(row => row.Status == "created"));
        var importedDirect = await Send<AiKnowledgeFaqDraftWorkspace>(HttpMethod.Get, drafts + "/" + imported.Items[0].FaqId, owner);
        Assert.AreEqual(draft.SourceHash, importedDirect.SourceHash); Assert.AreEqual(draft.Feedback, importedDirect.Feedback);
        var stripped = package with { Items = [package.Items[0] with { Feedback = null }] };
        await Expect(HttpMethod.Post, transfer + "/preview", owner, new PreviewAiKnowledgeFaqImport(stripped), HttpStatusCode.BadRequest);
        var lowTransfer = await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post, $"/api/v1/ai/knowledge-bases/{sourceKnowledge}/faq-transfer/preview", owner,
            new PreviewAiKnowledgeFaqImport(package));
        Assert.IsTrue(lowTransfer.Items.All(row => row.Status == "classification_conflict")); Assert.AreEqual(noCost, await Count()); Assert.AreEqual(noCalls, modelCalls());
        // 完整索引、发布与页面等待都可能超过令牌期限；负向来源断言前正式续取身份，避免过期拒绝冒充来源拒绝。
        if (browserReady is not null) (owner, reader, labelOnly) = await browserReady(review);
        else (owner, reader, labelOnly) = await renewActors();
        // 来源变化发生在真实根锁等待之后，不能以固定延迟代替等待观测或提前拒绝。
        await WaitedRejection(async () => { feedback = await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Delete, feedbackPath + "?version=" + feedback.Version, reader); }, "feedback_withdrawn");
        noCost = await Count();
        foreach (var id in new[] { draft.Id, child.Id, copy.Id, nextFeedback.Id, imported.Items[0].FaqId!.Value, imported.Items[1].FaqId!.Value })
        {
            await Expect(HttpMethod.Get, drafts + "/" + id, owner, null, HttpStatusCode.Conflict);
            await Expect(HttpMethod.Get, drafts + $"/{id}/revisions", owner, null, HttpStatusCode.Conflict);
        }
        await Expect(HttpMethod.Post, transfer + "/export", owner, new ExportAiKnowledgeFaqs([draft.Id]), HttpStatusCode.Conflict);
        var unavailable = await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post, transfer + "/preview", owner, new PreviewAiKnowledgeFaqImport(package));
        Assert.IsTrue(unavailable.Items.All(row => row.Status == "source_unavailable"));
        foreach (var history in new[] { directHistory, childHistory })
        {
            var hidden = await History(history); Assert.IsNotNull(hidden.KnowledgeUnavailableCode); Assert.AreEqual("", hidden.Content); Assert.AreEqual(0, hidden.Citations.Count);
        }
        await Expect(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{childHistory.SessionId}/knowledge/ask", reader,
            new AskAiKnowledgeRequest([target.Id], embedding, "继续说明", scope), seedHistory is null ? HttpStatusCode.Conflict : HttpStatusCode.Forbidden);
        await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", reader,
            new SearchAiKnowledgeRequest([target.Id], [childSource.DocumentId], embedding, embeddingVersion, "标准", 20, false, [childSource]), HttpStatusCode.Conflict);
        Assert.AreEqual(first.Id, (await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get, Published(sourceKnowledge, first.Id), reader)).FaqId);
        Assert.IsNull((await History(original)).KnowledgeUnavailableCode); Assert.AreEqual(noCost, await Count());
        if (browserRevoked is not null) await browserRevoked();
        feedback = await Send<AiKnowledgeAnswerFeedbackResponse>(HttpMethod.Put, feedbackPath, reader,
            new SetAiKnowledgeAnswerFeedbackRequest("helpful", "accurate", [originalAnswer.Citations[0].Id], feedback.Version));
        review = await Review("approved", feedback.Version, review.ReviewVersion);
        await WaitedRejection(async () => { review = await Review("rejected", feedback.Version, review.ReviewVersion); }, "review_rejected");
        await Expect(HttpMethod.Get, drafts + "/" + draft.Id, owner, null, HttpStatusCode.Conflict);
        Assert.AreEqual(files, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
        Assert.AreEqual(noCalls, modelCalls());

        async Task WaitedRejection(Func<Task> invalidate, string reason)
        {
            var waitingTarget = await Target(owner, "根锁等待后复核反馈 " + reason, []);
            var waitingDrafts = Drafts(waitingTarget.Id);
            var frozen = await Send<AiKnowledgeFaqFeedbackPreview>(HttpMethod.Post, waitingDrafts + "/feedback-preview", owner, new PreviewAiKnowledgeFaqFeedback(feedback.Id));
            var intent = new CreateAiKnowledgeFaqFeedbackDraft(Guid.CreateVersion7(), feedback.Id, frozen.Digest, content);
            var mysql = db is MySqlConnection;
            await using var blocker = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql, connection); await blocker.OpenAsync();
            var observerString = mysql ? new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync()) { Database = db.Database }.ConnectionString : connection;
            await using var observer = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql, observerString); await observer.OpenAsync();
            await using var held = await blocker.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            Task<HttpResponseMessage>? pending = null; var released = false;
            try
            {
                Assert.AreEqual(waitingTarget.Id, await blocker.QuerySingleAsync<Guid>(mysql ? "SELECT Id FROM fn_ai_knowledge_base WHERE Id=@Id FOR UPDATE" : "SELECT Id FROM fn_ai_knowledge_base WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id", new { Id = waitingTarget.Id }, held));
                var session = await blocker.QuerySingleAsync<long>(mysql ? "SELECT CONNECTION_ID()" : "SELECT @@SPID", transaction: held);
                pending = Request(HttpMethod.Post, waitingDrafts + "/from-feedback", owner, intent);
                var waitSql = mysql ? "SELECT COUNT(*) FROM performance_schema.data_lock_waits AS waits INNER JOIN performance_schema.data_locks AS locks ON locks.ENGINE_LOCK_ID=waits.BLOCKING_ENGINE_LOCK_ID INNER JOIN performance_schema.threads AS holder ON holder.THREAD_ID=locks.THREAD_ID WHERE locks.OBJECT_SCHEMA=@Database AND locks.OBJECT_NAME='fn_ai_knowledge_base' AND holder.PROCESSLIST_ID=@Session"
                    : "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID(@Database) AND blocking_session_id=@Session AND wait_type LIKE 'LCK%'";
                var until = DateTimeOffset.UtcNow.AddSeconds(12); var waited = false;
                while (!pending.IsCompleted && DateTimeOffset.UtcNow < until)
                {
                    waited = await observer.QuerySingleAsync<int>(waitSql, new { Database = db.Database, Session = session }, commandTimeout: 2) > 0;
                    if (waited) break; await Task.Delay(25);
                }
                Assert.IsTrue(waited, "必须观察实际知识库根锁等待，不能用早期来源拒绝代替事务最后复核。");
                await invalidate(); await held.CommitAsync(); released = true;
                using var rejected = await pending.WaitAsync(TimeSpan.FromSeconds(40));
                Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode, await rejected.Content.ReadAsStringAsync());
                Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq WHERE KnowledgeBaseId=@Id", new { Id = waitingTarget.Id }));
                Console.WriteLine($"FAQ feedback root wait: {(mysql ? "MySql" : "SqlServer")}; tenant={seedHistory is not null}; reason={reason}; rejected_without_writes=true");
            }
            finally
            {
                if (!released) await held.RollbackAsync();
                if (pending is not null && !pending.IsCompleted) try { using var drained = await pending.WaitAsync(TimeSpan.FromSeconds(40)); } catch (Exception) { }
            }
        }
        async Task PrivateHistory((Guid SessionId, Guid MessageId) identity, bool direct)
        {
            var json = await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id", new { Id = identity.MessageId });
            var value = JsonNode.Parse(json)!; Assert.AreEqual(7, value["version"]!.GetValue<int>());
            var faq = value["dependencies"]![0]!["faq"]!;
            Assert.AreEqual(direct, faq["feedbackDerived"]?.GetValue<bool>() == true);
            if (!direct) Assert.IsTrue(faq["graph"]!["nodes"]!.AsArray().Any(node => node!["source"]!["feedbackDerived"]?.GetValue<bool>() == true));
            var downgraded = value.DeepClone(); downgraded["version"] = 6;
            downgraded["dependencies"]![0]!["faq"]!.AsObject().Remove("feedbackDerived");
            if (!direct) foreach (var node in downgraded["dependencies"]![0]!["faq"]!["graph"]!["nodes"]!.AsArray()) node!["source"]!.AsObject().Remove("feedbackDerived");
            await db.ExecuteAsync("UPDATE fn_ai_chat_message SET KnowledgeLineage=@Json WHERE Id=@Id", new { Json = downgraded.ToJsonString(), Id = identity.MessageId });
            try { var hidden = await History(identity); Assert.IsNotNull(hidden.KnowledgeUnavailableCode); Assert.AreEqual("", hidden.Content); }
            finally { await db.ExecuteAsync("UPDATE fn_ai_chat_message SET KnowledgeLineage=@Json WHERE Id=@Id", new { Json = json, Id = identity.MessageId }); }
            Assert.IsNull((await History(identity)).KnowledgeUnavailableCode);
        }
        async Task<(Guid SessionId, Guid MessageId)> Answer(AiKnowledgeSearchSource source, string question)
        {
            var ask = new AskAiKnowledgeRequest([source.KnowledgeBaseId], embedding, question, new([source.DocumentId], embeddingVersion, [source]));
            if (seedHistory is not null)
            {
                await Expect(HttpMethod.Post, "/api/v1/ai/chat/sessions", reader, new CreateAiChatSessionRequest(generation, "租户写入边界"), HttpStatusCode.Forbidden);
                var seeded = await seedHistory(source);
                await Expect(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{seeded.SessionId}/knowledge/ask", reader, ask, HttpStatusCode.Forbidden); return seeded;
            }
            var chat = await Send<AiChatSessionResponse>(HttpMethod.Post, "/api/v1/ai/chat/sessions", reader, new CreateAiChatSessionRequest(generation, "私人反馈真实来源"), HttpStatusCode.Created);
            var answer = await Send<AskAiKnowledgeResponse>(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask", reader, ask);
            Assert.IsFalse(answer.Refused); Assert.IsNull(answer.Message.KnowledgeUnavailableCode); return (chat.Id, answer.Message.Id);
        }
        Task<AiKnowledgeAnswerFeedbackReviewItem> Review(string decision, int feedbackVersion, int reviewVersion) => Send<AiKnowledgeAnswerFeedbackReviewItem>(HttpMethod.Put,
            $"/api/v1/ai/knowledge-answer-feedback/reviews/{feedback.Id}", owner, new ReviewAiKnowledgeAnswerFeedbackRequest(decision, feedbackVersion, reviewVersion));
        async Task<AiKnowledgeBaseResponse> Target(string actor, string title, Guid[] readers)
        {
            var value = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", actor, new CreateAiKnowledgeBaseRequest(title, null), HttpStatusCode.Created);
            var path = $"/api/v1/ai/knowledge-bases/{value.Id}";
            value = await Send<AiKnowledgeBaseResponse>(HttpMethod.Put, path + "/policy", actor, new UpdateAiKnowledgePolicyRequest("restricted", embedding, embeddingVersion, generation, generationVersion, value.Version));
            if (readers.Length > 0) await Send<AiKnowledgeMembersResponse>(HttpMethod.Put, path + "/members", actor, new SetAiKnowledgeMembersRequest(readers, value.Version));
            return await Send<AiKnowledgeBaseResponse>(HttpMethod.Get, path, actor);
        }
        Task<AiKnowledgeSearchResponse> Search(Guid library, Guid document) => Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", reader,
            new SearchAiKnowledgeRequest([library], [document], embedding, embeddingVersion, "标准", 20, false, null));
        async Task<AiChatMessageResponse> History((Guid SessionId, Guid MessageId) identity) => (await Send<AiChatSessionResponse>(HttpMethod.Get,
            $"/api/v1/ai/chat/sessions/{identity.SessionId}", reader)).Messages.Single(message => message.Id == identity.MessageId);
        Task<int> Count() => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        static string Drafts(Guid library) => $"/api/v1/ai/knowledge-bases/{library}/faq-drafts";
        static string Published(Guid library, Guid faq) => $"/api/v1/ai/knowledge-bases/{library}/faqs/{faq}";
        async Task<T> Send<T>(HttpMethod method, string path, string actor, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var response = await Request(method, path, actor, body); var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expected, response.StatusCode, $"{method} {path}: {text}"); return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        async Task Expect(HttpMethod method, string path, string actor, object? body, HttpStatusCode expected)
        { using var response = await Request(method, path, actor, body); Assert.AreEqual(expected, response.StatusCode, $"{method} {path}: {await response.Content.ReadAsStringAsync()}"); }
        async Task<HttpResponseMessage> Request(HttpMethod method, string path, string actor, object? body)
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new("Bearer", actor);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType()); return await client.SendAsync(request);
        }
    }
}
