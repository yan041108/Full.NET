using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>不可变 Prompt 输入策略；文本仅替换固定 input 占位符，工作流只使用显式绑定的注册节点模板。</summary>
/// <param name="ModelConfigId">受当前作用域准入的模型配置。</param>
/// <param name="LanguageTag">规范语言标签，本阶段仅支持 en-US 或 zh-CN。</param>
/// <param name="Template">包含且仅包含一次 {{input}} 的文本模板，最多 8192 个字符。</param>
/// <param name="InputTokenLimit">该版本的输入 Token 预算预留上限；启用严格准入且批准同 Responses 协议时精确计数后派发，其他模式不宣称请求硬限。</param>
/// <param name="OutputTokenLimit">该版本允许的输出 Token 预算预留上限；单次文本 Run 的请求上限同时受此预算与提供程序 4096 上限约束，实际计量仍由模型回执处理。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiPromptBody(Guid ModelConfigId, string LanguageTag, string Template, long InputTokenLimit, long OutputTokenLimit)
{
    /// <summary>显式版本二静态执行绑定；缺失时保持版本一原正文和摘要，不推断新能力。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiPromptExecutionBinding? Execution { get; init; }
}

/// <summary>版本二只绑定服务端现有静态定义；工具和 Schema 必须与注册目录完全一致。</summary>
/// <param name="PolicyVersion">固定策略版本二，与数据库序列化封装版本独立。</param>
/// <param name="DefinitionKey">已注册的静态 Agent 或工作流定义。</param>
/// <param name="DefinitionVersion">精确静态定义版本。</param>
/// <param name="DefinitionDigest">服务端静态拓扑或 Agent 定义摘要。</param>
/// <param name="InputSchemaKey">固定输入 Schema 标识。</param>
/// <param name="OutputSchemaKey">固定输出 Schema 标识。</param>
/// <param name="AllowedToolNames">静态允许工具的完整有序集合，不接受远程工具扩张。</param>
/// <param name="RetrievalStrategyKey">固定为 none；检索策略需独立的真实评估与激活凭据。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiPromptExecutionBinding(int PolicyVersion, string DefinitionKey, int DefinitionVersion, string DefinitionDigest,
    string InputSchemaKey, string OutputSchemaKey, IReadOnlyList<string> AllowedToolNames, string RetrievalStrategyKey);

/// <summary>创建 Prompt 目录及首个不可变草稿版本；创建不会自动发布。</summary>
/// <param name="ClientRequestId">本人、当前作用域内的幂等键。</param>
/// <param name="Name">目录显示名称，不参与业务判断。</param>
/// <param name="Body">首版不可变策略。</param>
public sealed record CreateAiPromptRequest(Guid ClientRequestId, string Name, AiPromptBody Body);

/// <summary>追加不可变草稿，不覆盖旧版或当前发布指针。</summary>
/// <param name="Revision">目录预期修订号。</param>
/// <param name="Body">待追加的不可变策略。</param>
public sealed record AppendAiPromptVersionRequest(int Revision, AiPromptBody Body);

/// <summary>发布或回滚到目录中的指定版本；回滚只允许曾经发布的版本。</summary>
/// <param name="Revision">目录预期修订号。</param>
/// <param name="VersionId">目标不可变版本。</param>
public sealed record SelectAiPromptVersionRequest(int Revision, Guid VersionId);

/// <summary>停用目录并增加撤销代次，阻止旧代次运行被重新激活。</summary>
/// <param name="Revision">目录预期修订号。</param>
public sealed record DeactivateAiPromptRequest(int Revision);

/// <summary>当前作用域可读的 Prompt 目录状态。</summary>
/// <param name="Id">目录标识。</param>
/// <param name="Name">目录显示名称。</param>
/// <param name="Revision">并发修订号。</param>
/// <param name="LastVersion">最后追加的版本号。</param>
/// <param name="ActiveVersionId">当前发布版本；为空表示停用或尚未发布。</param>
/// <param name="Epoch">停用撤销代次。</param>
public sealed record AiPromptResponse(Guid Id, string Name, int Revision, int LastVersion, Guid? ActiveVersionId, int Epoch);

/// <summary>不可变版本及首次发布标记；不包含模型凭据或端点。</summary>
/// <param name="Id">版本标识。</param>
/// <param name="PromptId">所属目录。</param>
/// <param name="Version">不可变版本号。</param>
/// <param name="Body">不可变文本策略。</param>
/// <param name="ModelConfigVersion">发布所绑定的模型配置修订号。</param>
/// <param name="ProviderKey">提供程序稳定键。</param>
/// <param name="ModelId">模型稳定标识。</param>
/// <param name="BodyHash">绑定策略和模型身份的 SHA256 摘要。</param>
/// <param name="WasPublished">是否曾经显式发布。</param>
/// <param name="CreatedAtUtc">创建时间。</param>
public sealed record AiPromptVersionResponse(Guid Id, Guid PromptId, int Version, AiPromptBody Body,
    int ModelConfigVersion, string ProviderKey, string ModelId, string BodyHash, bool WasPublished, DateTimeOffset CreatedAtUtc)
{
    /// <summary>本格式只允许已注册的纯文本定义。</summary>
    public string DefinitionKey => Body.Execution?.DefinitionKey ?? "fullnet-single-text-v1";
    /// <summary>当前绑定的精确静态定义版本。</summary>
    public int DefinitionVersion => Body.Execution?.DefinitionVersion ?? 1;
    /// <summary>策略正文版本；旧正文保持一。</summary>
    public int PolicyVersion => Body.Execution?.PolicyVersion ?? 1;
    /// <summary>输入采用固定纯文本 Schema。</summary>
    public string InputSchemaKey => Body.Execution?.InputSchemaKey ?? "ai.prompt.text.v1";
    /// <summary>输出采用固定纯文本 Schema，模型输出仍是不可信数据。</summary>
    public string OutputSchemaKey => Body.Execution?.OutputSchemaKey ?? "ai.prompt.text.v1";
    /// <summary>纯文本版本不授予工具调用。</summary>
    public IReadOnlyList<string> AllowedToolNames => Body.Execution?.AllowedToolNames ?? Array.Empty<string>();
    /// <summary>本格式不发起检索。</summary>
    public string RetrievalStrategyKey => Body.Execution?.RetrievalStrategyKey ?? "none";
}

/// <summary>Prompt 管理与消费分别授权，客户端隐藏不能代替服务端复核。</summary>
public static class AiPromptPermissions
{
    /// <summary>读取当前作用域的目录及版本。</summary>
    public const string Read = "ai.prompts.read";
    /// <summary>创建目录及首版草稿。</summary>
    public const string Create = "ai.prompts.create";
    /// <summary>追加不可变草稿版本。</summary>
    public const string Append = "ai.prompts.append";
    /// <summary>显式发布草稿。</summary>
    public const string Publish = "ai.prompts.publish";
    /// <summary>停用目录并撤销旧运行代次。</summary>
    public const string Deactivate = "ai.prompts.deactivate";
    /// <summary>回滚到曾经发布的版本。</summary>
    public const string Rollback = "ai.prompts.rollback";
    /// <summary>在本人纯文本 Run 使用当前发布版本。</summary>
    public const string Use = "ai.prompts.use";
}

/// <summary>Prompt 版本管理的稳定机器错误码。</summary>
public static class AiPromptErrorCodes
{
    /// <summary>资源来源的完整代码段前缀。</summary>
    public const string Prefix = "ai.prompts.";
    /// <summary>输入或操作目标不合法。</summary>
    public const string Invalid = Prefix + "invalid";
    /// <summary>并发修订或幂等载荷冲突。</summary>
    public const string Conflict = Prefix + "conflict";
    /// <summary>当前范围不存在该目录或版本。</summary>
    public const string NotFound = Prefix + "not_found";
    /// <summary>发布策略、正文或模型准入不可用。</summary>
    public const string Unavailable = Prefix + "unavailable";
}
