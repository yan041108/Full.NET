using System.Text.RegularExpressions;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>聊天内容与敏感信息边界校验。</summary>
internal static partial class AiChatContentPolicy
{
    /// <summary>单条用户消息允许的最大字符数。</summary>
    internal const int MaxUserMessageLength = 8000;

    /// <summary>带入模型的历史消息条数上限。</summary>
    internal const int MaxHistoryMessages = 40;

    /// <summary>默认会话标题。</summary>
    internal const string DefaultSessionTitle = "新对话";

    /// <summary>根据首条用户消息生成会话标题。</summary>
    /// <param name="content">用户消息正文。</param>
    /// <returns>截断后的标题。</returns>
    public static string BuildTitleFromMessage(string content)
    {
        var normalized = content.Trim().ReplaceLineEndings(" ");
        if (normalized.Length <= 48)
        {
            return normalized;
        }

        return normalized[..48] + "...";
    }

    /// <summary>校验用户消息是否可发送。</summary>
    /// <param name="content">用户消息正文。</param>
    /// <returns>校验失败时的错误消息；成功时为 <see langword="null"/>。</returns>
    public static string? ValidateUserMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Message content is required.";
        }

        if (content.Length > MaxUserMessageLength)
        {
            return $"Message content must not exceed {MaxUserMessageLength} characters.";
        }

        if (LikelySecretPattern().IsMatch(content))
        {
            return "Message appears to contain API keys or secrets. Remove secrets before sending.";
        }

        return null;
    }

    /// <summary>截断对外错误文本，避免回显过长提供程序响应。</summary>
    /// <param name="message">原始错误文本。</param>
    /// <returns>截断后的文本。</returns>
    public static string SanitizeExternalError(string message) =>
        message.Length <= 512 ? message : message[..512];

    [GeneratedRegex(@"(?i)(sk-[a-z0-9]{10,}|api[_-]?key\s*[:=]|bearer\s+[a-z0-9._-]{20,})")]
    private static partial Regex LikelySecretPattern();
}
