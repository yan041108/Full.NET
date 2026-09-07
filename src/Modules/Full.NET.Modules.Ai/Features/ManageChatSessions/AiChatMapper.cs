using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;

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
    public static AiChatMessageResponse MapMessage(AiChatMessageRecord row) =>
        new(
            row.Id,
            row.SessionId,
            row.RoleKey,
            row.Content,
            row.StatusKey,
            row.PromptTokens,
            row.CompletionTokens,
            row.CreatedAtUtc);

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
}
