using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>静态 Agent Tool 目录；禁止从 Controller 或 HTTP 回环自动发现工具。</summary>
internal static class AiAgentToolCatalog
{
    private static readonly AiAgentToolCatalogItem[] Tools =
    [
        new(
            "ai.tools.ping",
            "Ping",
            "Returns a static health payload without side effects.",
            AiAgentToolPermissions.CatalogRead,
            AiAgentToolSideEffectKeys.None,
            """{"type":"object","additionalProperties":false,"properties":{}}""",
            """{"type":"object","required":["ok"],"properties":{"ok":{"type":"boolean"}}}""",
            true,
            "internal"),
        new(
            "ai.models.list",
            "List AI model configs",
            "Lists enabled AI model configurations for the current scope.",
            AiModelPermissions.Read,
            AiAgentToolSideEffectKeys.Read,
            """{"type":"object","additionalProperties":false,"properties":{"page":{"type":"integer","minimum":1},"pageSize":{"type":"integer","minimum":1,"maximum":100}}}""",
            """{"type":"object","required":["items"],"properties":{"items":{"type":"array"}}}""",
            true,
            "planned"),
        new(
            "ai.chat.sessions.list",
            "List AI chat sessions",
            "Lists chat sessions owned by the current user.",
            AiChatPermissions.Read,
            AiAgentToolSideEffectKeys.Read,
            """{"type":"object","additionalProperties":false,"properties":{"page":{"type":"integer","minimum":1},"pageSize":{"type":"integer","minimum":1,"maximum":100}}}""",
            """{"type":"object","required":["items"],"properties":{"items":{"type":"array"}}}""",
            true,
            "planned"),
    ];

    /// <summary>返回全部已登记的只读工具目录项。</summary>
    public static IReadOnlyList<AiAgentToolCatalogItem> List() => Tools;

    /// <summary>按工具名查找目录项。</summary>
    /// <param name="toolName">工具名。</param>
    public static AiAgentToolCatalogItem? Find(string toolName) =>
        Tools.FirstOrDefault(item =>
            string.Equals(item.ToolName, toolName, StringComparison.Ordinal));
}
