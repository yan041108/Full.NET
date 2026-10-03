namespace Full.NET.Modules.Ai.Contracts;

/// <summary>知识库稳定错误码；业务不依赖译文。</summary>
public static class AiKnowledgeErrorCodes
{
    /// <summary>来源文件名、内容类型、实际内容或大小不符合上传边界。</summary>
    public const string UploadInvalid = Prefix + "upload_invalid";
    /// <summary>当前导入已被有界租约持有。</summary>
    public const string ImportBusy = Prefix + "import_busy";
    /// <summary>稳定重试键已用于不同的上传请求。</summary>
    public const string ImportConflict = Prefix + "import_conflict";
    /// <summary>持久化导入尚缺来源文件，须以原重试键重传。</summary>
    public const string UploadRequired = Prefix + "upload_required";
    /// <summary>权威来源文件不存在或元数据不匹配。</summary>
    public const string SourceUnavailable = Prefix + "source_unavailable";
    /// <summary>文档不存在、已删除、目录禁用或当前主体未获授权。</summary>
    public const string DocumentNotFound = Prefix + "document_not_found";
    /// <summary>文档元数据或版本输入不符合边界。</summary>
    public const string DocumentInvalid = Prefix + "document_invalid";
    /// <summary>文档元数据、删除或授权版本竞争。</summary>
    public const string DocumentVersionConflict = Prefix + "document_version_conflict";
    /// <summary>文档授权用户不是当前知识库的明确成员。</summary>
    public const string DocumentMemberUnavailable = Prefix + "document_member_unavailable";
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
