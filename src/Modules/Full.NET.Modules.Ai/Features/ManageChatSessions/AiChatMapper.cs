using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManageChatSessions;

/// <summary>聊天会话与消息响应映射。</summary>
internal static class AiChatMapper
{
    /// <summary>映射会话列表项。</summary>
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
    public static AiChatSessionResponse MapDetail(
        AiChatSessionRecord row,
        IReadOnlyList<AiChatMessageRecord> messages) =>
        new(
            row.Id,
            row.TenantId,
            row.OwnerUserId,
            row.ModelConfigId,
            row.ModelName,
            row.Title,
            row.IsGenerating,
            messages.Select(MapMessage).ToArray(),
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);
}
