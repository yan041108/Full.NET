namespace Full.NET.Modules.Ai.Contracts;

/// <summary>知识库稳定错误码；业务不依赖译文。</summary>
public static class AiKnowledgeErrorCodes
{
    /// <summary>错误资源匹配前缀。</summary>
    public const string Prefix = "ai.knowledge.";
    /// <summary>目录或审批输入不合法。</summary>
    public const string InputInvalid = Prefix + "input_invalid";
    /// <summary>分页超出边界。</summary>
    public const string PageInvalid = Prefix + "page_invalid";
    /// <summary>知识库不存在或不归当前主体。</summary>
    public const string NotFound = Prefix + "not_found";
    /// <summary>目录或审批版本竞争。</summary>
    public const string VersionConflict = Prefix + "version_conflict";
    /// <summary>批准的精确模型配置版本不可用。</summary>
    public const string ModelUnavailable = Prefix + "model_unavailable";
}
