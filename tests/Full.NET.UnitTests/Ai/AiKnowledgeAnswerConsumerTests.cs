using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Ai.Mcp;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Text.Json;

namespace Full.NET.UnitTests.Ai;

/// <summary>未经来源资格复核的派生正文不能从默认映射释放；普通消息维持原行为。</summary>
[TestClass]
public sealed class AiKnowledgeAnswerConsumerTests
{
    [TestMethod] [DataRow(true, null)] [DataRow(true, "{}")] [DataRow(false, "{}")]
    public void Default_mapping_refuses_derived_or_inconsistent_message_body(bool marked, string? metadata)
    {
        var message = new AiChatMessageRecord { Content = "受保护知识正文", IsKnowledgeAnswer = marked, KnowledgeLineage = metadata };
        Assert.AreEqual("", AiChatMapper.MapMessage(message).Content);
    }
    [TestMethod]
    public void Ordinary_message_keeps_original_body()
        => Assert.AreEqual("普通消息", AiChatMapper.MapMessage(new AiChatMessageRecord { Content = "普通消息" }).Content);

    [TestMethod] [DataRow("valid")] [DataRow("message")] [DataRow("session")] [DataRow("role")] [DataRow("status")] [DataRow("marker")]
    public void Authorized_mapping_releases_only_bound_completed_assistant_and_safe_citations(string fault)
    {
        var lineage = AiKnowledgeAnswerLineageTests.Lineage();
        var row = new AiChatMessageRecord { Id = fault == "message" ? Guid.CreateVersion7() : lineage.MessageId,
            SessionId = fault == "session" ? Guid.CreateVersion7() : lineage.SessionId, Content = "知识答案",
            RoleKey = fault == "role" ? "user" : "assistant", StatusKey = fault == "status" ? "streaming" : "completed",
            IsKnowledgeAnswer = fault != "marker", KnowledgeLineage = AiKnowledgeAnswerLineageCodec.Encode(lineage) };
        var response = AiChatMapper.MapMessage(row, lineage);
        Assert.AreEqual(fault == "valid" ? "知识答案" : "", response.Content);
        Assert.AreEqual(fault == "valid" ? null : AiErrorCodes.KnowledgeAnswerUnavailable, response.KnowledgeUnavailableCode);
        Assert.AreEqual(fault == "valid" ? 1 : 0, response.Citations.Count);
        var context = new AiJsonSerializerContext(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var json = JsonSerializer.Serialize(response, context.AiChatMessageResponse);
        foreach (var hidden in new[] { "knowledgeLineage", "dependencies", "receiptHash", "manifestHash", "contentHash" })
            Assert.DoesNotContain(hidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task Missing_guard_keeps_session_and_mcp_summary_closed_to_derived_body()
    {
        var lineage = AiKnowledgeAnswerLineageTests.Lineage(); var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        var messages = new AiChatMessageRecord[] { new() { Id = lineage.MessageId, SessionId = lineage.SessionId,
            RoleKey = "assistant", Content = "知识正文不能出现", StatusKey = "completed", IsKnowledgeAnswer = true,
            KnowledgeLineage = AiKnowledgeAnswerLineageCodec.Encode(lineage) }, new() { Content = "普通消息", RoleKey = "user" } };
        var service = new AiChatSessionQueryService(new Query(new() { Id = lineage.SessionId, OwnerUserId = lineage.ActorUserId }, messages),
            tenant, Options.Create(new DatabaseOptions()), Substitute.For<IClock>());
        var result = await service.GetByIdAsync(lineage.SessionId, lineage.ActorUserId);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("", result.Value!.Messages[0].Content);
        Assert.AreEqual(AiErrorCodes.KnowledgeAnswerUnavailable, result.Value.Messages[0].KnowledgeUnavailableCode);
        var auth = Substitute.For<IToolAuthorizationPort>();
        auth.AuthorizeAsync(AiChatPermissions.Read, Arg.Any<CancellationToken>()).Returns(new ToolActor(lineage.ActorUserId, null, Guid.CreateVersion7()));
        var summary = await new AiMcpSessionSummaryReader(service, auth).TryReadSummaryJsonAsync($"fullnet://ai/chat/sessions/{lineage.SessionId}/summary");
        Assert.IsNotNull(summary); Assert.DoesNotContain("知识正文不能出现", summary, StringComparison.Ordinal);
    }
    private sealed class Query(AiChatSessionRecord session, AiChatMessageRecord[] messages) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<T>)(object)messages);
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult((T?)(object)session);
    }
}
