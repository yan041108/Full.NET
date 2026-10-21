using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAnswerFeedback;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库服务验证隔离、回滚与来源撤权；HTTP 验证版本及真实引用，均不调用模型。</summary>
internal static class AiKnowledgeAnswerFeedbackAssertions
{
    internal static async Task<AiKnowledgeAnswerFeedbackResponse> HttpAsync(Func<HttpMethod, string, HttpContent?, Task<HttpResponseMessage>> send,
        Guid sessionId, AiChatMessageResponse message, Func<HttpMethod, string, HttpContent?, Task<HttpResponseMessage>>? stranger = null)
    {
        var path = $"/api/v1/ai/chat/sessions/{sessionId}/messages/{message.Id}/feedback";
        var request = new SetAiKnowledgeAnswerFeedbackRequest("helpful", "accurate", message.Citations.Select(c => c.Id).ToArray(), 0);
        using (var empty = await send(HttpMethod.Get, path, null)) await Expect(empty, HttpStatusCode.NotFound);
        var initial = await Put(request); Assert.AreEqual(1, initial.Version); Assert.AreEqual("pending_review", initial.StatusKey);
        Assert.AreEqual(initial.Id, (await Put(request)).Id);
        if (stranger is not null) foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        { using var denied = await stranger(method, path + (method == HttpMethod.Delete ? "?version=1" : ""), method == HttpMethod.Put ? JsonContent.Create(request) : null); await Expect(denied, HttpStatusCode.NotFound); }
        using (var read = await send(HttpMethod.Get, path, null)) { await Expect(read, HttpStatusCode.OK); Assert.AreEqual(1, (await read.Content.ReadFromJsonAsync<AiKnowledgeAnswerFeedbackResponse>())!.Version); }
        using (var forged = await send(HttpMethod.Put, path, JsonContent.Create(request with { Version = 1, CitationIds = [Guid.CreateVersion7()] }))) await Expect(forged, HttpStatusCode.BadRequest);
        using (var stale = await send(HttpMethod.Put, path, JsonContent.Create(request with { RatingKey = "unhelpful", ReasonKey = "other" }))) await Expect(stale, HttpStatusCode.Conflict);
        var updated = await Put(request with { Version = 1, ReasonKey = "useful" }); Assert.AreEqual(2, updated.Version);
        var withdrawn = await Delete(2); Assert.AreEqual(3, withdrawn.Version); Assert.AreEqual("withdrawn", withdrawn.StatusKey);
        Assert.IsNull(withdrawn.RatingKey); Assert.IsNull(withdrawn.ReasonKey); Assert.HasCount(0, withdrawn.CitationIds);
        Assert.AreEqual(withdrawn.Version, (await Delete(2)).Version);
        using (var old = await send(HttpMethod.Put, path, JsonContent.Create(request with { Version = 1, ReasonKey = "useful" }))) await Expect(old, HttpStatusCode.Conflict);
        var renewed = await Put(request with { Version = 3 }); Assert.AreEqual(4, renewed.Version);
        var raced = await Task.WhenAll(send(HttpMethod.Put, path, JsonContent.Create(request with { Version = 4, ReasonKey = "useful" })),
            send(HttpMethod.Put, path, JsonContent.Create(request with { Version = 4, RatingKey = "unhelpful", ReasonKey = "other" })));
        try { CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, raced.Select(r => r.StatusCode).ToArray()); }
        finally { foreach (var response in raced) response.Dispose(); }
        using var final = await send(HttpMethod.Get, path, null); await Expect(final, HttpStatusCode.OK);
        using (var state = await send(HttpMethod.Get, path + "/state", null))
        {
            await Expect(state, HttpStatusCode.OK);
            var json = await state.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            CollectionAssert.AreEquivalent(new[] { "messageId", "statusKey", "version" }, json.EnumerateObject().Select(p => p.Name).ToArray());
        }
        var result = await final.Content.ReadFromJsonAsync<AiKnowledgeAnswerFeedbackResponse>(); Assert.IsNotNull(result); Assert.AreEqual(5, result.Version); return result;
        async Task<AiKnowledgeAnswerFeedbackResponse> Put(SetAiKnowledgeAnswerFeedbackRequest body)
        { using var response = await send(HttpMethod.Put, path, JsonContent.Create(body)); await Expect(response, HttpStatusCode.OK); var result = await response.Content.ReadFromJsonAsync<AiKnowledgeAnswerFeedbackResponse>(); Assert.IsNotNull(result); return result; }
        async Task<AiKnowledgeAnswerFeedbackResponse> Delete(int version)
        { using var response = await send(HttpMethod.Delete, path + $"?version={version}", null); await Expect(response, HttpStatusCode.OK); var result = await response.Content.ReadFromJsonAsync<AiKnowledgeAnswerFeedbackResponse>(); Assert.IsNotNull(result); return result; }
    }
    internal static async Task ServiceAsync(IServiceProvider services, DbConnection db, AiKnowledgeAnswerHistoryGuard guard,
        ICurrentSessionAuthorization authorization, Guid actor, Guid sessionId, Guid messageId, Guid documentId)
    {
        var query = services.GetRequiredService<IQueryExecutor>(); var commands = services.GetRequiredService<ICommandExecutor>();
        var transaction = services.GetRequiredService<ICommandTransaction>(); var state = services.GetRequiredService<IDataTransactionState>();
        var tenant = services.GetRequiredService<ICurrentTenant>(); var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
        var database = services.GetRequiredService<IOptions<DatabaseOptions>>(); var ids = services.GetRequiredService<IIdGenerator>(); var clock = services.GetRequiredService<IClock>();
        AiKnowledgeAnswerFeedbackService Create(ICommandExecutor command, ICurrentSessionAuthorization? currentAuthorization = null) => new(query, command, transaction, state, tenant, currentAuthorization ?? authorization, guard, database, ids, clock);
        var service = Create(commands); var request = new SetAiKnowledgeAnswerFeedbackRequest("helpful", "accurate", [], 0);
        var p = new { MessageId = messageId };
        Assert.IsFalse((await Create(new RejectAfterWrite(commands)).SetAsync(sessionId, messageId, actor, request, default)).IsSuccess);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_answer_feedback WHERE MessageId=@MessageId", p));
        await Assert.ThrowsAsync<OperationCanceledException>(() => Create(new CancelAfterWrite(commands)).SetAsync(sessionId, messageId, actor, request, default));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_answer_feedback WHERE MessageId=@MessageId", p));
        var created = await service.SetAsync(sessionId, messageId, actor, request, default); Assert.IsTrue(created.IsSuccess, created.Error?.ToString());
        Assert.AreEqual(1, created.Value!.Version);
        // 提交与清除是独立能力；即使其他来源权限齐全，也不能越过当前精确操作授权。
        var clearOnly = Create(commands, new DenyPermission(authorization, AiKnowledgeAnswerFeedbackPermissions.Submit));
        Assert.IsFalse((await clearOnly.SetAsync(sessionId, messageId, actor, request with { Version = 1 }, default)).IsSuccess);
        Assert.IsTrue((await clearOnly.GetStateAsync(sessionId, messageId, actor, default)).IsSuccess);
        var submitOnly = Create(commands, new DenyPermission(authorization, AiKnowledgeAnswerFeedbackPermissions.Withdraw));
        Assert.IsFalse((await submitOnly.GetStateAsync(sessionId, messageId, actor, default)).IsSuccess);
        Assert.IsFalse((await submitOnly.WithdrawAsync(sessionId, messageId, actor, 1, default)).IsSuccess);
        Assert.IsFalse((await service.GetAsync(sessionId, messageId, Guid.CreateVersion7(), default)).IsSuccess);
        if (tenant.Id is { } tenantId)
        {
            writer.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "其他租户"));
            try { Assert.IsFalse((await service.GetAsync(sessionId, messageId, actor, default)).IsSuccess); Assert.IsFalse((await service.WithdrawAsync(sessionId, messageId, actor, 1, default)).IsSuccess); }
            finally { writer.SetTenant(new TenantContext(tenantId, "test", "测试租户")); }
        }
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@Id", new { Id = documentId });
        try
        {
            Assert.IsFalse((await service.GetAsync(sessionId, messageId, actor, default)).IsSuccess);
            Assert.IsFalse((await service.SetAsync(sessionId, messageId, actor, request with { Version = 1 }, default)).IsSuccess);
            var clearing = await service.GetStateAsync(sessionId, messageId, actor, default); Assert.IsTrue(clearing.IsSuccess); Assert.AreEqual(1, clearing.Value!.Version);
            Assert.IsFalse((await service.GetStateAsync(sessionId, messageId, Guid.CreateVersion7(), default)).IsSuccess);
            var withdrawn = await service.WithdrawAsync(sessionId, messageId, actor, 1, default); Assert.IsTrue(withdrawn.IsSuccess); Assert.AreEqual("withdrawn", withdrawn.Value!.StatusKey);
        }
        finally { await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=0 WHERE Id=@Id", new { Id = documentId }); }
        Assert.IsTrue((await service.SetAsync(sessionId, messageId, actor, request with { Version = 2 }, default)).IsSuccess);
        Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_answer_feedback WHERE MessageId=@MessageId", p));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_answer_feedback WHERE MessageId=@MessageId", p));
    }
    internal static async Task Expect(HttpResponseMessage response, HttpStatusCode expected) => Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
    private sealed class RejectAfterWrite(ICommandExecutor inner) : ICommandExecutor
    { public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) { await inner.ExecuteAsync(statement, parameters, cancellationToken); return 0; } }
    private sealed class CancelAfterWrite(ICommandExecutor inner) : ICommandExecutor
    { public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) { await inner.ExecuteAsync(statement, parameters, cancellationToken); throw new OperationCanceledException(); } }
    private sealed class DenyPermission(ICurrentSessionAuthorization inner, string denied) : ICurrentSessionAuthorization
    {
        public Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default) => permissionCode == denied ? Task.FromResult<AuthorizedSessionActor?>(null) : inner.AuthorizeAsync(permissionCode, cancellationToken);
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default) => permissionCodes.Contains(denied, StringComparer.Ordinal) ? Task.FromResult<AuthorizedSessionActor?>(null) : inner.AuthorizeAllAsync(permissionCodes, cancellationToken);
    }
}
