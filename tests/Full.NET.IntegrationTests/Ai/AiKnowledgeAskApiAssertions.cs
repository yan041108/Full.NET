using System.Text.Json;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Time;
using Full.NET.AI.Abstractions.Models;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用已完成 TXT/PDF 来源，真实 HTTP/身份/双库/账本验证公开问答；模型输出受控。</summary>
internal static class AiKnowledgeAskApiAssertions
{
    internal static async Task<long> VerifyAsync(FullNetApiFactory original, DbConnection db, AiKnowledgeIndexTaskBinding source, AiModelConfigRecord embedding)
    {
        var model = new AiKnowledgePaidAnswerAssertions.ControlledFactory();
        using var factory = new FullNetApiFactory(original.Provider, original.ConnectionString, configureTestServices: services =>
        {
            services.RemoveAll<IClock>(); services.AddSingleton<IClock, LiveClock>();
            services.RemoveAll<IAiModelClientFactory>(); services.AddSingleton<IAiModelClientFactory>(model);
        });
        var actor = await factory.CreateHostIdentityAsync($"ask-{Guid.NewGuid():N}", [AiChatPermissions.Create, AiChatPermissions.Read, AiChatPermissions.Send,
            AiChatPermissions.Cancel, AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead,
            AiKnowledgeAnswerFeedbackPermissions.Read, AiKnowledgeAnswerFeedbackPermissions.Submit, AiKnowledgeAnswerFeedbackPermissions.Withdraw]);
        var other = await factory.CreateHostIdentityAsync($"ask-other-{Guid.NewGuid():N}", [AiChatPermissions.Send, AiChatPermissions.Read, AiKnowledgeAnswerFeedbackPermissions.Read, AiKnowledgeAnswerFeedbackPermissions.Submit, AiKnowledgeAnswerFeedbackPermissions.Withdraw]);
        var memberId = Guid.CreateVersion7(); var docMemberId = Guid.CreateVersion7();
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_member (Id,KnowledgeBaseId,UserId,CreatedAtUtc) VALUES (@Id,@BaseId,@Actor,@Now)",
            new { Id = memberId, BaseId = source.KnowledgeBaseId, Actor = actor.UserId, Now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@Id,@DocumentId,@Actor,@Now)",
            new { Id = docMemberId, source.DocumentId, Actor = actor.UserId, Now = DateTimeOffset.UtcNow });
        var generationId = Guid.CreateVersion7();
        await db.ExecuteAsync("INSERT INTO fn_ai_model_config (Id,TenantId,Name,ProviderKey,EndpointBaseUrl,ModelId,IsDefault,IsEnabled,CreatedAtUtc,Version) VALUES (@Id,NULL,@Name,@Provider,@Endpoint,'gpt-4.1-mini',0,1,@Now,1)",
            new { Id = generationId, Name = "公开问答生成测试", Provider = embedding.ProviderKey, Endpoint = embedding.EndpointBaseUrl, Now = DateTimeOffset.UtcNow });
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", actor.AccessToken);
        using var created = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(generationId, "公开问答"));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var chat = await created.Content.ReadFromJsonAsync<AiChatSessionResponse>(); Assert.IsNotNull(chat);
        var path = $"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask";
        var request = new AskAiKnowledgeRequest([source.KnowledgeBaseId], embedding.Id, "来源说明");
        const string routedPath = "/api/v1/ai/tasks/knowledge-ask";
        var routed = new RouteAiTaskRequest("knowledge_ask", chat.Id, request);
        var before = await Charges();
        using (var invalidRoute = await client.PostAsJsonAsync(routedPath, routed with { IntentKey = "unknown" }))
            await AiAgentRunApiAssertions.AssertProblemAsync(invalidRoute, HttpStatusCode.BadRequest, "ai.routing.invalid");
        using (var invalid = await client.PostAsJsonAsync(path, request with { Question = "" }))
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, invalid.StatusCode, await invalid.Content.ReadAsStringAsync());
        using (var stranger = factory.CreateClientForHost("localhost"))
        {
            stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", other.AccessToken);
            using var forbidden = await stranger.PostAsJsonAsync(path, request);
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode, await forbidden.Content.ReadAsStringAsync());
            using var routeForbidden = await stranger.PostAsJsonAsync(routedPath, routed);
            Assert.AreEqual(HttpStatusCode.Forbidden, routeForbidden.StatusCode, await routeForbidden.Content.ReadAsStringAsync());
        }
        Assert.AreEqual(before, await Charges());
        using var otherClient = factory.CreateClientForHost("localhost");
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", other.AccessToken);
        var feedback = new List<AiKnowledgeAnswerFeedbackResponse>();
        async Task<HttpResponseMessage> Send(HttpMethod method, string route, HttpContent? content) => await client.SendAsync(new HttpRequestMessage(method, route) { Content = content });
        async Task<HttpResponseMessage> Stranger(HttpMethod method, string route, HttpContent? content) => await otherClient.SendAsync(new HttpRequestMessage(method, route) { Content = content });
        foreach (var refused in new[] { false, true })
        {
            model.Refused = refused;
            using var response = refused ? await client.PostAsJsonAsync(path, request) : await client.PostAsJsonAsync(routedPath, routed);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            var result = await response.Content.ReadFromJsonAsync<AskAiKnowledgeResponse>(); Assert.IsNotNull(result);
            Assert.IsTrue(result.Message.IsKnowledgeAnswer);
            feedback.Add(await AiKnowledgeAnswerFeedbackAssertions.HttpAsync(Send, chat.Id, result.Message, Stranger));
            Assert.AreEqual(refused, result.Refused); Assert.AreEqual(refused ? "" : "真实索引受控问答", result.Message.Content);
            Assert.AreEqual(refused ? 0 : 1, result.Message.Citations.Count); Assert.IsNull(result.Message.KnowledgeUnavailableCode);
            if (!refused) { Assert.AreEqual(source.DocumentId, result.Message.Citations[0].DocumentId); Assert.AreEqual(source.VersionId, result.Message.Citations[0].VersionId); }
            Assert.AreEqual(result.Message.Content, await db.QuerySingleAsync<string>("SELECT Content FROM fn_ai_chat_message WHERE Id=@Id", new { result.Message.Id }));
            Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("KnowledgeLineage", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_chat_session WHERE Id=@Id AND IsGenerating=1", new { Id = chat.Id }));
        }
        // 提供程序返回前撤回未引用完整来源的访问资格，正文不能先于最终权威复核交付。
        model.Refused = false;
        model.BeforeChatReturn = () => db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_member WHERE Id=@Id", new { Id = docMemberId });
        using (var revoked = await client.PostAsJsonAsync(path, request))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, revoked.StatusCode, await revoked.Content.ReadAsStringAsync());
            Assert.AreEqual("ai.knowledge.answer_unavailable", (await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            Assert.IsFalse((await revoked.Content.ReadAsStringAsync()).Contains("真实索引受控问答", StringComparison.Ordinal));
        }
        model.BeforeChatReturn = null;
        foreach (var item in feedback)
        {
            var feedbackPath = $"/api/v1/ai/chat/sessions/{chat.Id}/messages/{item.MessageId}/feedback";
            using var hidden = await client.GetAsync(feedbackPath); await AiKnowledgeAnswerFeedbackAssertions.Expect(hidden, HttpStatusCode.NotFound);
            using var state = await client.GetAsync(feedbackPath + "/state"); await AiKnowledgeAnswerFeedbackAssertions.Expect(state, HttpStatusCode.OK);
            var clearState = await state.Content.ReadFromJsonAsync<AiKnowledgeAnswerFeedbackStateResponse>(); Assert.IsNotNull(clearState); Assert.AreEqual(item.Version, clearState.Version);
            using var denied = await client.PutAsJsonAsync(feedbackPath, new SetAiKnowledgeAnswerFeedbackRequest("helpful", "accurate", [], item.Version)); await AiKnowledgeAnswerFeedbackAssertions.Expect(denied, HttpStatusCode.NotFound);
            using var withdrawn = await client.DeleteAsync(feedbackPath + $"?version={item.Version}"); await AiKnowledgeAnswerFeedbackAssertions.Expect(withdrawn, HttpStatusCode.OK);
        }
        Assert.AreEqual(6L, await db.QuerySingleAsync<long>("SELECT MessageCount FROM fn_ai_chat_session WHERE Id=@Id", new { Id = chat.Id }));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_chat_message WHERE SessionId=@Id AND StatusKey='streaming'", new { Id = chat.Id }));
        var failed = await db.QuerySingleAsync<string>("SELECT Content FROM fn_ai_chat_message WHERE SessionId=@Id AND IsKnowledgeAnswer=1 AND StatusKey='failed'", new { Id = chat.Id });
        Assert.AreEqual("", failed);
        using var history = await client.GetAsync($"/api/v1/ai/chat/sessions/{chat.Id}");
        Assert.AreEqual(HttpStatusCode.OK, history.StatusCode, await history.Content.ReadAsStringAsync());
        var detail = await history.Content.ReadFromJsonAsync<AiChatSessionResponse>(); Assert.IsNotNull(detail);
        Assert.IsTrue(detail.Messages.Where(message => message.RoleKey == "assistant").All(message => message.Content.Length == 0 && message.Citations.Count == 0));
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@Id,@DocumentId,@Actor,@Now)",
            new { Id = docMemberId, source.DocumentId, Actor = actor.UserId, Now = DateTimeOffset.UtcNow });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.BeforeChatReturn = () => { entered.TrySetResult(); return release.Task; };
        var pending = client.PostAsJsonAsync(path, request);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var busy = await client.PostAsJsonAsync(path, request);
            Assert.AreEqual(HttpStatusCode.Conflict, busy.StatusCode, await busy.Content.ReadAsStringAsync());
            using var cancel = await client.PostAsync($"/api/v1/ai/chat/sessions/{chat.Id}/cancel", null);
            Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
            using var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.AreEqual(HttpStatusCode.Conflict, cancelled.StatusCode, await cancelled.Content.ReadAsStringAsync());
            Assert.IsFalse((await cancelled.Content.ReadAsStringAsync()).Contains("真实索引受控问答", StringComparison.Ordinal));
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_chat_message WHERE SessionId=@Id AND IsKnowledgeAnswer=1 AND StatusKey='cancelled' AND Content='' AND KnowledgeLineage IS NULL", new { Id = chat.Id }));
            Assert.AreEqual(8L, await db.QuerySingleAsync<long>("SELECT MessageCount FROM fn_ai_chat_session WHERE Id=@Id AND IsGenerating=0", new { Id = chat.Id }));
        }
        finally { release.TrySetResult(); model.BeforeChatReturn = null; using var ignored = await pending; }
        return await Charges() - before;

        Task<long> Charges() => db.QuerySingleAsync<long>("SELECT COALESCE(SUM(ChargedTokens),0) FROM fn_ai_operation_budget WHERE ModelConfigId=@Id", new { Id = embedding.Id });
    }
    private sealed class LiveClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
