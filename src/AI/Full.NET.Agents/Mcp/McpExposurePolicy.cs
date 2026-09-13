namespace Full.NET.Agents.Mcp;

/// <summary>MCP 工具暴露策略；远端 readOnlyHint 不能替代本地策略。</summary>
public static class McpExposurePolicy
{
    /// <summary>已通过 MCP 对外暴露。</summary>
    public const string ExposedKey = "exposed";

    /// <summary>仅管理 API/Agent 内部可见。</summary>
    public const string InternalKey = "internal";

    /// <summary>尚未开放 MCP 暴露。</summary>
    public const string PlannedKey = "planned";

    /// <summary>会话摘要资源 URI 模板。</summary>
    public const string SessionSummaryUriTemplate = "fullnet://ai/chat/sessions/{sessionId}/summary";

    /// <summary>静态会话摘要提示名。</summary>
    public const string SessionSummaryPromptName = "chat-session-summary";

    /// <summary>判断目录项是否允许通过 MCP 宣告。</summary>
    public static bool IsCatalogItemExposable(
        string mcpExposureKey,
        string sideEffectKey,
        bool isEnabled,
        bool hasHandler) =>
        isEnabled
        && hasHandler
        && string.Equals(mcpExposureKey, ExposedKey, StringComparison.Ordinal)
        && sideEffectKey is "none" or "read";
}
