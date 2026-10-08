using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.Modules.Ai.Features.ManageChatSessions;

/// <summary>聊天会话与消息响应映射。</summary>
internal static class AiChatMapper
{
    /// <summary>映射会话列表项。</summary>
    /// <param name="row">持久化会话。</param>
    public static AiChatSessionListItem MapListItem(AiChatSessionRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.OwnerUserId,
            row.ModelConfigId,
            row.ModelName,
            row.Title,
            row.MessageCount,
            row.LastMessageAtUtc,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    /// <summary>映射消息响应。</summary>
    /// <param name="row">持久化消息。</param>
    public static AiChatMessageResponse MapMessage(AiChatMessageRecord row) => MapMessage(row, null);

    /// <summary>只有完整复核且绑定当前消息的依赖才允许释放知识答案。</summary>
    internal static AiChatMessageResponse MapMessage(AiChatMessageRecord row, AiKnowledgeAnswerLineage? verified)
    {
        var derived = row.IsKnowledgeAnswer || row.KnowledgeLineage is not null;
        var available = derived && row.IsKnowledgeAnswer && row.RoleKey == AiChatMessageRoleKeys.Assistant
            && row.StatusKey == AiChatMessageStatusKeys.Completed && verified is not null
            && verified.MessageId == row.Id && verified.SessionId == row.SessionId;
        return new(
            row.Id,
            row.SessionId,
            row.RoleKey,
            derived && !available ? "" : row.Content,
            row.StatusKey,
            row.PromptTokens,
            row.CompletionTokens,
            row.CreatedAtUtc)
        {
            IsKnowledgeAnswer = derived,
            KnowledgeUnavailableCode = derived && !available ? AiErrorCodes.KnowledgeAnswerUnavailable : null,
            Citations = available ? verified!.References.Select(reference => new AiKnowledgeCitationResponse(reference.Id,
                reference.DocumentId, reference.VersionId, reference.Sequence, reference.PageNumber, reference.Offset, reference.Length) { DocxLocation = reference.DocxLocation }).ToArray() : [],
        };
    }

    /// <summary>映射会话详情。</summary>
    /// <param name="row">持久化会话。</param>
    /// <param name="messages">已授权的消息集合。</param>
    /// <param name="now">当前 UTC 时间，过期槽位不能让客户端永久等待。</param>
    public static AiChatSessionResponse MapDetail(
        AiChatSessionRecord row,
        IReadOnlyList<AiChatMessageRecord> messages,
        DateTimeOffset now) =>
        new(
            row.Id,
            row.TenantId,
            row.OwnerUserId,
            row.ModelConfigId,
            row.ModelName,
            row.Title,
            row.IsGenerating && row.GenerationExpiresAtUtc > now,
            messages.Select(MapMessage).ToArray(),
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    /// <summary>批量来源复核完成后同步映射，避免在逐条映射之间加入异步等待。</summary>
    internal static AiChatSessionResponse MapAuthorizedDetail(AiChatSessionRecord row, IReadOnlyList<AiChatMessageRecord> messages,
        DateTimeOffset now, IReadOnlyDictionary<Guid, AiKnowledgeAnswerLineage> verified) =>
        MapDetail(row, [], now) with { Messages = messages.Select(message => MapMessage(message, verified.GetValueOrDefault(message.Id))).ToArray() };
}
