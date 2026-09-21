namespace Full.NET.Modules.Ai.Contracts;

/// <summary>聊天消息角色键。</summary>
public static class AiChatMessageRoleKeys
{
    /// <summary>用户消息。</summary>
    public const string User = "user";

    /// <summary>助手回复。</summary>
    public const string Assistant = "assistant";

    /// <summary>系统提示。</summary>
    public const string System = "system";
}

/// <summary>聊天消息状态键。</summary>
public static class AiChatMessageStatusKeys
{
    /// <summary>流式生成中。</summary>
    public const string Streaming = "streaming";

    /// <summary>已完成。</summary>
    public const string Completed = "completed";

    /// <summary>用户取消。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>生成失败。</summary>
    public const string Failed = "failed";
}

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>聊天会话列表项。</summary>
/// <param name="Id">会话标识。</param>
/// <param name="TenantId">租户标识；Host 会话为空。</param>
/// <param name="OwnerUserId">所属用户标识。</param>
/// <param name="ModelConfigId">绑定的模型配置标识。</param>
/// <param name="ModelName">模型配置显示名称快照。</param>
/// <param name="Title">会话标题。</param>
/// <param name="MessageCount">消息条数。</param>
/// <param name="LastMessageAtUtc">最近消息时间（UTC）。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record AiChatSessionListItem(
    Guid Id,
    Guid? TenantId,
    Guid OwnerUserId,
    Guid ModelConfigId,
    string ModelName,
    string Title,
    int MessageCount,
    DateTimeOffset? LastMessageAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>聊天消息响应。</summary>
/// <param name="Id">消息标识。</param>
/// <param name="SessionId">会话标识。</param>
/// <param name="RoleKey">角色键。</param>
/// <param name="Content">消息正文。</param>
/// <param name="StatusKey">状态键。</param>
/// <param name="PromptTokens">提示 Token 数。</param>
/// <param name="CompletionTokens">补全 Token 数。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
public sealed record AiChatMessageResponse(
    Guid Id,
    Guid SessionId,
    string RoleKey,
    string Content,
    string StatusKey,
    int? PromptTokens,
    int? CompletionTokens,
    DateTimeOffset CreatedAtUtc);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>聊天会话详情。</summary>
/// <param name="Id">会话标识。</param>
/// <param name="TenantId">租户标识；Host 会话为空。</param>
/// <param name="OwnerUserId">所属用户标识。</param>
/// <param name="ModelConfigId">绑定的模型配置标识。</param>
/// <param name="ModelName">模型配置显示名称快照。</param>
/// <param name="Title">会话标题。</param>
/// <param name="IsGenerating">是否正在流式生成。</param>
/// <param name="Messages">按时间排序的消息列表。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record AiChatSessionResponse(
    Guid Id,
    Guid? TenantId,
    Guid OwnerUserId,
    Guid ModelConfigId,
    string ModelName,
    string Title,
    bool IsGenerating,
    IReadOnlyList<AiChatMessageResponse> Messages,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>创建聊天会话请求。</summary>
/// <param name="ModelConfigId">模型配置标识。</param>
/// <param name="Title">可选标题；为空时使用默认标题。</param>
public sealed record CreateAiChatSessionRequest(
    Guid ModelConfigId,
    string? Title);

/// <summary>更新聊天会话请求。</summary>
/// <param name="Title">新标题。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record UpdateAiChatSessionRequest(
    string Title,
    int Version);

/// <summary>发送聊天消息并启动流式回复的请求。</summary>
/// <param name="Content">用户消息正文。</param>
public sealed record StreamAiChatMessageRequest(string Content);

/// <summary>流式事件：增量文本。</summary>
/// <param name="Delta">增量文本片段。</param>
public sealed record AiChatStreamDeltaEvent(string Delta);

/// <summary>流式事件：完成摘要。</summary>
/// <param name="AssistantMessageId">助手消息标识。</param>
/// <param name="PromptTokens">提示 Token 数。</param>
/// <param name="CompletionTokens">补全 Token 数。</param>
public sealed record AiChatStreamDoneEvent(
    Guid AssistantMessageId,
    int? PromptTokens,
    int? CompletionTokens);

/// <summary>流式事件：错误摘要。</summary>
/// <param name="Message">错误说明。</param>
public sealed record AiChatStreamErrorEvent(string Message);
