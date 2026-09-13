using System.Text.RegularExpressions;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>聊天内容与敏感信息边界校验。</summary>
internal static partial class AiChatContentPolicy
{
    /// <summary>单条用户消息允许的最大字符数。</summary>
    internal const int MaxUserMessageLength = 8000;

    /// <summary>带入模型的历史消息条数上限。</summary>
    internal const int MaxHistoryMessages = 40;

    /// <summary>发送给模型的历史正文总字符上限。</summary>
    internal const int MaxHistoryCharacters = 64000;

    /// <summary>单次请求向提供程序声明的最大生成 Token 数。</summary>
    internal const int MaxCompletionTokens = 4096;

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

    /// <summary>不回显不可信异常正文，避免供应商把凭据或业务数据带入客户端错误。</summary>
    /// <param name="message">原始错误文本。</param>
    /// <returns>固定安全说明。</returns>
    public static string SanitizeExternalError(string message) =>
        "AI request failed. Please retry or contact the administrator.";

    /// <summary>识别明显的凭据内容，防止用户把密钥意外发送给外部模型。</summary>
    [GeneratedRegex(@"(?i)(sk-[a-z0-9]{10,}|api[_-]?key\s*[:=]|bearer\s+[a-z0-9._-]{20,})")]
    private static partial Regex LikelySecretPattern();
}
