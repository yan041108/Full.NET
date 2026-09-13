using System.Text.Json;
using Full.NET.Agents.Mcp;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Mcp.Serialization;

namespace Full.NET.Modules.Ai.Mcp;

/// <summary>按受限 URI 返回当前主体可读的会话摘要 JSON。</summary>
internal sealed class AiMcpSessionSummaryReader(
    AiChatSessionQueryService sessions,
    IToolAuthorizationPort authorization) : IMcpSessionSummaryReader
{
    public async ValueTask<string?> TryReadSummaryJsonAsync(
        string resourceUri,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseSessionSummaryUri(resourceUri, out var sessionId))
        {
            return null;
        }

        var actor = await authorization.AuthorizeAsync(AiChatPermissions.Read, cancellationToken)
            .ConfigureAwait(false);
        if (actor is null)
        {
            return null;
        }

        var result = await sessions.GetByIdAsync(sessionId, actor.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return null;
        }

        var session = result.Value;
        var summary = new McpChatSessionSummary(
            session.Id,
            session.Title,
            session.ModelName,
            session.Messages.Count,
            session.Messages.LastOrDefault()?.CreatedAtUtc,
            session.Messages.TakeLast(3).Select(message => new McpChatSessionMessageSummary(
                message.RoleKey,
                Truncate(message.Content, 256))).ToArray());
        return JsonSerializer.Serialize(summary, AiMcpJsonSerializerContext.Default.McpChatSessionSummary);
    }

    private static bool TryParseSessionSummaryUri(string resourceUri, out Guid sessionId)
    {
        sessionId = default;
        const string prefix = "fullnet://ai/chat/sessions/";
        if (!resourceUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = resourceUri[prefix.Length..];
        if (!remainder.EndsWith("/summary", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var idPart = remainder[..^"/summary".Length];
        return Guid.TryParse(idPart, out sessionId);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed record McpChatSessionSummary(
    Guid SessionId,
    string Title,
    string ModelName,
    int MessageCount,
    DateTimeOffset? LastMessageAtUtc,
    IReadOnlyList<McpChatSessionMessageSummary> RecentMessages);

internal sealed record McpChatSessionMessageSummary(string RoleKey, string ContentPreview);
