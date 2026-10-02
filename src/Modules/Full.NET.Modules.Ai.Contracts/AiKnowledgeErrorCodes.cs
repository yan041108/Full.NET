namespace Full.NET.Modules.Ai.Contracts;

/// <summary>知识库稳定错误码；业务不依赖译文。</summary>
public static class AiKnowledgeErrorCodes
{
    /// <summary>成员集合或并发版本不符合输入约束。</summary>
    public const string MembersInvalid = "ai.knowledge.members_invalid";
    /// <summary>目标用户不存在、已停用或不属于当前租户。</summary>
    public const string MemberUnavailable = "ai.knowledge.member_unavailable";
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
