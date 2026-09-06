namespace Full.NET.Modules.Ai.Contracts;

/// <summary>AI 聊天会话权限码。</summary>
public static class AiChatPermissions
{
    /// <summary>读取聊天会话与历史。</summary>
    public const string Read = "ai.chat.sessions.read";

    /// <summary>创建聊天会话。</summary>
    public const string Create = "ai.chat.sessions.create";

    /// <summary>更新聊天会话（重命名等）。</summary>
    public const string Update = "ai.chat.sessions.update";

    /// <summary>删除聊天会话。</summary>
    public const string Delete = "ai.chat.sessions.delete";

    /// <summary>发送消息并接收流式回复。</summary>
    public const string Send = "ai.chat.messages.send";

    /// <summary>取消进行中的流式生成。</summary>
    public const string Cancel = "ai.chat.messages.cancel";
}
