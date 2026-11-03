using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>以真实公开请求证明 FAQ 不是普通文件、不是无来源文本，也不把未选中的叶正文加入排名。</summary>
internal static class FaqSearchAssertions
{
    internal static async Task VerifyAsync(HttpClient client, DbConnection database, Guid knowledge, Guid parent, Guid faq, Guid document,
        Guid revision, Guid publication, Guid embedding, int embeddingVersion, Guid generation, int generationVersion,
        string owner, string reader, Guid[] members, Func<Task> replaceParent,
        Func<AiKnowledgeSearchSource, Task>? browserReady = null, Func<Task>? browserReplaced = null,
        Func<AiKnowledgeSearchSource, Task<(Guid SessionId, Guid MessageId)>>? seedReadOnlyHistory = null,
        Func<Guid,Guid,Task>? verifyHistory = null, int expectedLineageVersion = 5)
    {
        const string searchPath = "/api/v1/ai/knowledge-search"; var root = $"/api/v1/ai/knowledge-bases/{knowledge}";
        var input = new SearchAiKnowledgeRequest([knowledge], [document], embedding, embeddingVersion, "标准", 20, false, null);
        var before = await Count();
        var found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, searchPath, reader, input);
        Assert.AreEqual(1, found.Sources.Count); Assert.IsTrue(found.Hits.Count > 0);
        var source = found.Sources.Single(); Assert.AreEqual("faq-revision-v1", source.SourceKind); Assert.AreEqual(faq, source.FaqId); Assert.AreEqual(revision, source.FaqRevisionId); Assert.AreEqual(publication, source.PublicationId);
        Assert.IsTrue(found.Hits.All(value => value.Source == source && value.Correction is null && value.PageNumber is null && value.SourceLocation is null && value.DocxLocation is null));
        Assert.IsFalse(found.Sources.Any(value => value.DocumentId == parent)); Assert.AreEqual(before, await Count());
        await Expect(HttpMethod.Get, root + $"/documents/{document}", reader, null, HttpStatusCode.NotFound);
        await Expect(HttpMethod.Post, searchPath, reader, input with { ExpectedSources = [source with { SourceKind = "document-v1" }] }, HttpStatusCode.BadRequest);
        await Expect(HttpMethod.Post, searchPath, reader, input with { ExpectedSources = [source with { SourceKind = "document-v1", FaqId = null, FaqRevisionId = null }] }, HttpStatusCode.Conflict);
        await Expect(HttpMethod.Post, searchPath, reader, input with { ExpectedSources = [source with { FaqId = Guid.CreateVersion7() }] }, HttpStatusCode.Conflict);
        var context = await Send<AiKnowledgeSearchContextResponse>(HttpMethod.Post, searchPath + "/context", reader,
            new ReadAiKnowledgeSearchContextRequest(source, embedding, embeddingVersion, found.Hits[0].Sequence, 0));
        Assert.AreEqual(source, context.Source); Assert.IsTrue(context.Blocks.All(value => value.PageNumber is null && value.SourceLocation is null)); Assert.AreEqual(before, await Count());
        var scope = new AiKnowledgeSearchAnswerScope([document], embeddingVersion, [source]);
        var compared = await Send<AiKnowledgeSearchComparisonResponse>(HttpMethod.Post, searchPath + "/compare", reader,
            new CompareAiKnowledgeSearchRequest([knowledge], embedding, input.Query, 20, false, scope));
        Assert.AreEqual(source, compared.Sources.Single()); Assert.IsTrue(compared.Vector.Hits.All(value => value.Source == source));
        var refined = await Send<AiKnowledgeSearchRefinementResponse>(HttpMethod.Post, searchPath + "/refine", reader,
            new RefineAiKnowledgeSearchRequest([knowledge], embedding, generation, generationVersion, input.Query, "rewrite_rerank", 8, false, scope));
        Assert.AreEqual(source, refined.Sources.Single()); Assert.AreEqual(4, refined.Operations.Count); Assert.AreEqual(4, refined.Operations.Select(value => value.OperationId).Distinct().Count());
        var ask = new AskAiKnowledgeRequest([knowledge], embedding, input.Query, scope);
        Guid sessionId; Guid messageId; AiChatMessageResponse answer;
        if (seedReadOnlyHistory is null)
        {
            var chat = await Send<AiChatSessionResponse>(HttpMethod.Post, "/api/v1/ai/chat/sessions", reader,
                new CreateAiChatSessionRequest(generation, "FAQ 完整谱系"), HttpStatusCode.Created);
            sessionId = chat.Id;
            var answered = await Send<AskAiKnowledgeResponse>(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{sessionId}/knowledge/ask", reader, ask);
            Assert.IsFalse(answered.Refused); answer = answered.Message; messageId = answer.Id;
        }
        else
        {
            // 既有聊天写入只开放 Host；租户以真实只读证明建立历史夹具，不能为验收扩大正式权限。
            var noCost = await Count();
            await Expect(HttpMethod.Post, "/api/v1/ai/chat/sessions", reader, new CreateAiChatSessionRequest(generation, "租户拒绝写入"), HttpStatusCode.Forbidden);
            (sessionId, messageId) = await seedReadOnlyHistory(source); answer = await History();
            await Expect(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{sessionId}/knowledge/ask", reader, ask, HttpStatusCode.Forbidden);
            Assert.AreEqual(noCost, await Count());
        }
        Assert.AreEqual("assistant", answer.RoleKey); Assert.IsNull(answer.KnowledgeUnavailableCode); Assert.IsTrue(answer.Citations.Count > 0);
        Assert.IsTrue(answer.Citations.All(value => value.FaqSource?.Source == source));
        var stored = await database.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id", new { Id = answer.Id });
        using (var json = JsonDocument.Parse(stored))
        {
            Assert.AreEqual(expectedLineageVersion, json.RootElement.GetProperty("version").GetInt32());
            var node = json.RootElement.GetProperty("dependencies")[0]; Assert.AreEqual(JsonValueKind.Null, node.GetProperty("fileId").ValueKind);
            Assert.AreEqual(faq, node.GetProperty("faq").GetProperty("faqId").GetGuid());
            Assert.AreEqual(parent, node.GetProperty("faq").GetProperty("dependencies")[0].GetProperty("documentId").GetGuid());
            Assert.AreEqual(1, json.RootElement.GetProperty("dependencies").GetArrayLength());
        }
        var history = await History(); Assert.IsNull(history.KnowledgeUnavailableCode); Assert.AreEqual(source, history.Citations.Single().FaqSource!.Source);
        if (verifyHistory is not null) await verifyHistory(sessionId,messageId);
        var downgraded = System.Text.Json.Nodes.JsonNode.Parse(stored)!;
        downgraded["dependencies"]![0]!.AsObject().Remove("faq"); downgraded["dependencies"]![0]!["fileId"] = Guid.CreateVersion7().ToString();
        await database.ExecuteAsync("UPDATE fn_ai_chat_message SET KnowledgeLineage=@Json WHERE Id=@Id", new { Json = downgraded.ToJsonString(), Id = answer.Id });
        try { await Unavailable(); }
        finally { await database.ExecuteAsync("UPDATE fn_ai_chat_message SET KnowledgeLineage=@Json WHERE Id=@Id", new { Json = stored, Id = answer.Id }); }
        var originalProvenance = await database.QuerySingleAsync<string>("SELECT SourceJson FROM fn_ai_knowledge_faq WHERE Id=@Id", new { Id = faq });
        await database.ExecuteAsync("UPDATE fn_ai_knowledge_faq SET SourceJson='{' WHERE Id=@Id", new { Id = faq });
        try
        {
            var noCost = await Count(); await Expect(HttpMethod.Post, searchPath, reader, input, HttpStatusCode.Conflict);
            await Unavailable(); Assert.AreEqual(noCost, await Count());
        }
        finally { await database.ExecuteAsync("UPDATE fn_ai_knowledge_faq SET SourceJson=@Json WHERE Id=@Id", new { Json = originalProvenance, Id = faq }); }
        // 同版本目的地漂移也拒绝读取，不等到下一次收费派发才发现批准模型已变化。
        await database.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl='https://invalid.example.test/v1' WHERE Id=@Id", new { Id = embedding });
        try
        {
            var noCost = await Count(); await Expect(HttpMethod.Post, searchPath, reader, input, HttpStatusCode.Conflict);
            await Expect(HttpMethod.Get, root + $"/faqs/{faq}", reader, null, HttpStatusCode.Conflict); await Unavailable(); Assert.AreEqual(noCost, await Count());
        }
        finally { await database.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl='https://api.openai.com/v1' WHERE Id=@Id", new { Id = embedding }); }
        var parentRow = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, root + $"/documents/{parent}", owner);
        await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, root + $"/documents/{parent}/members", owner, new SetAiKnowledgeDocumentMembersRequest([], parentRow.Version));
        var beforeDenied = await Count(); await Expect(HttpMethod.Post, searchPath, reader, input, HttpStatusCode.Conflict); await Unavailable(); Assert.AreEqual(beforeDenied, await Count());
        parentRow = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, root + $"/documents/{parent}", owner);
        await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, root + $"/documents/{parent}/members", owner, new SetAiKnowledgeDocumentMembersRequest(members, parentRow.Version));
        Assert.IsNull((await History()).KnowledgeUnavailableCode);
        if (browserReady is not null) await browserReady(source);
        await replaceParent(); var afterReplace = await Count();
        await Expect(HttpMethod.Post, searchPath, reader, input with { ExpectedSources = [source] }, HttpStatusCode.Conflict);
        await Expect(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{sessionId}/knowledge/ask", reader, ask,
            seedReadOnlyHistory is null ? HttpStatusCode.Conflict : HttpStatusCode.Forbidden);
        await Unavailable(); Assert.AreEqual(afterReplace, await Count());
        if (browserReplaced is not null) await browserReplaced();

        Task<int> Count() => database.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        async Task<AiChatMessageResponse> History() => (await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{sessionId}", reader)).Messages.Single(value => value.Id == messageId);
        async Task Unavailable() { var value = await History(); Assert.IsNotNull(value.KnowledgeUnavailableCode); Assert.AreEqual("", value.Content); Assert.AreEqual(0, value.Citations.Count); }
        async Task<T> Send<T>(HttpMethod method, string uri, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var request = new HttpRequestMessage(method, uri); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            using var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expected, response.StatusCode, $"FAQ source request {method} {uri}: {text}");
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        async Task Expect(HttpMethod method, string uri, string token, object? body, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(method, uri); request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            using var response = await client.SendAsync(request); Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}
