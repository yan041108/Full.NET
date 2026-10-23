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
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>服务器保存的知识答案标记，包括拒答；当前来源资格由不可用机器码独立表示。</summary>
    public bool IsKnowledgeAnswer { get; init; }
    /// <summary>来源复核未通过时的稳定机器码；正文和引用同时隐藏。</summary>
    public string? KnowledgeUnavailableCode { get; init; }
    /// <summary>仅返回服务器确认且当前可读的引用位置。</summary>
    public IReadOnlyList<AiKnowledgeCitationResponse> Citations { get; init; } = [];
}

/// <summary>知识答案的来源位置，不包含内部完成证明或原始来源正文。</summary>
/// <param name="Id">服务器引用标识。</param>
/// <param name="DocumentId">来源文档标识。</param>
/// <param name="VersionId">来源版本标识。</param>
/// <param name="Sequence">分块序号。</param>
/// <param name="PageNumber">PDF 页码，TXT/DOCX 来源为空。</param>
/// <param name="Offset">分块偏移。</param>
/// <param name="Length">分块长度。</param>
public sealed record AiKnowledgeCitationResponse(Guid Id, Guid DocumentId, Guid VersionId, int Sequence, int? PageNumber, int Offset, int Length)
{
    /// <summary>DOCX 的逻辑段落/表格位置；旧 TXT/PDF 响应省略此字段。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AiKnowledgeDocxLocationResponse? DocxLocation { get; init; }
    /// <summary>服务器核对的统一来源位置；旧答案未保存此字段时省略。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Full.NET.Modules.Ai.Contracts.AiKnowledgeSourceLocationResponse? SourceLocation { get; init; }
}

/// <summary>DOCX 原始正文逻辑位置；不声明版式页码。</summary>
/// <param name="BlockSequence">逻辑正文块的零起始序号。</param>
/// <param name="ParagraphNumber">正文块的一起始段落编号。</param>
/// <param name="TableNumber">表格编号，普通段落为空。</param>
/// <param name="RowNumber">表格行号，普通段落为空。</param>
/// <param name="CellNumber">表格单元格号，普通段落为空。</param>
public sealed record AiKnowledgeDocxLocationResponse(int BlockSequence, int ParagraphNumber, int? TableNumber, int? RowNumber, int? CellNumber);

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
    int Version)
{
    /// <summary>当前消息窗口之前仍有历史，可使用第一条消息的时间和标识继续向前读取。</summary>
    public bool HasOlderMessages { get; init; }
}

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
