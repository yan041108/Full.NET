namespace Full.NET.Modules.Document.Contracts;

/// <summary>
/// Document 模块对外返回的稳定错误码目录。
/// </summary>
/// <remarks>
/// 机器码稳定性：发布后所有错误码字符串保持不变，新增只能追加；
/// 消费方应按前缀匹配或精确比较，不得依赖错误码的字典序或数值派生含义。
/// </remarks>
public static class DocumentErrorCodes
{
    /// <summary>Document 模块所有错误码的统一前缀。</summary>
    public const string Prefix = "document.";

    /// <summary>文档基础字段或业务状态校验失败。</summary>
    public const string Invalid = "document.host_document.invalid";

    /// <summary>目标文档不存在、已删除或属于其他租户。</summary>
    public const string NotFound = "document.host_document.not_found";

    /// <summary>乐观并发冲突：文档已被其他请求修改，请重新读取。</summary>
    public const string VersionConflict = "document.host_document.version_conflict";

    /// <summary>文档引用的文件标识不存在、未声明引用或属于其他租户。</summary>
    public const string InvalidFileReference = "document.host_document.invalid_file_reference";

    /// <summary>文档尚未发布任何当前版本，无法读取或预览。</summary>
    public const string NoCurrentVersion = "document.host_document.no_current_version";

    /// <summary>当前文件类型或存储后端不支持在线预览转换。</summary>
    public const string PreviewNotSupported = "document.host_document.preview_not_supported";

    /// <summary>文档分类基础字段或层级约束校验失败。</summary>
    public const string CategoryInvalid = "document.host_category.invalid";

    /// <summary>目标文档分类不存在、已删除或属于其他租户。</summary>
    public const string CategoryNotFound = "document.host_category.not_found";

    /// <summary>同一父分类下已存在同名分类，禁止重复创建。</summary>
    public const string CategoryNameExists = "document.host_category.name_exists";

    /// <summary>目标父分类不存在、已停用或会造成循环层级。</summary>
    public const string CategoryInvalidParent = "document.host_category.invalid_parent";

    /// <summary>乐观并发冲突：分类已被其他请求修改，请重新读取。</summary>
    public const string CategoryVersionConflict = "document.host_category.version_conflict";

    /// <summary>目标分类仍存在子分类，禁止直接删除。</summary>
    public const string CategoryHasChildren = "document.host_category.has_children";

    /// <summary>目标分类仍被文档或权限引用，禁止删除或禁用。</summary>
    public const string CategoryInUse = "document.host_category.in_use";

    /// <summary>文档标签基础字段或格式校验失败。</summary>
    public const string TagInvalid = "document.host_tag.invalid";

    /// <summary>目标文档标签不存在、已删除或属于其他租户。</summary>
    public const string TagNotFound = "document.host_tag.not_found";

    /// <summary>同一作用域下已存在同名标签，禁止重复创建。</summary>
    public const string TagNameExists = "document.host_tag.name_exists";

    /// <summary>乐观并发冲突：标签已被其他请求修改，请重新读取。</summary>
    public const string TagVersionConflict = "document.host_tag.version_conflict";

    /// <summary>目标标签仍被文档引用，禁止删除。</summary>
    public const string TagInUse = "document.host_tag.in_use";

    /// <summary>目标分享记录不存在、已撤销或属于其他租户。</summary>
    public const string ShareNotFound = "document.share.not_found";

    /// <summary>自定义分享码已被其他分享记录占用。</summary>
    public const string ShareCodeExists = "document.share.code_exists";

    /// <summary>分享基础字段、有效期或访问策略校验失败。</summary>
    public const string ShareInvalid = "document.share.invalid";

    /// <summary>文档权限授权字段或作用域校验失败。</summary>
    public const string PermissionInvalid = "document.permission.invalid";

    /// <summary>回收站条目不存在、已彻底清理或属于其他租户。</summary>
    public const string RecycleItemNotFound = "document.recycle.not_found";

    /// <summary>回收站批量永久清理过程中部分条目仍有未释放引用或并发冲突。</summary>
    public const string RecyclePurgeFailed = "document.host_recycle_bin.purge_failed";

    /// <summary>乐观并发冲突：分享设置已被其他请求修改，请重新读取。</summary>
    public const string ShareVersionConflict = "document.host_share.version_conflict";

    /// <summary>使用自定义分享码访问时未找到对应分享记录。</summary>
    public const string ShareCodeNotFound = "document.host_share.code_not_found";

    /// <summary>分享链接已超过有效期，访问被拒绝。</summary>
    public const string ShareExpired = "document.host_share.expired";

    /// <summary>分享链接已被创建者主动禁用，访问被拒绝。</summary>
    public const string ShareDisabled = "document.host_share.disabled";

    /// <summary>分享链接累计访问次数已达到创建者设定的上限。</summary>
    public const string ShareMaxAccessReached = "document.host_share.max_access_reached";

    /// <summary>访问该分享链接必须提供密码校验。</summary>
    public const string SharePasswordRequired = "document.host_share.password_required";

    /// <summary>当前后端版本尚不支持带密码保护的分享创建。</summary>
    public const string SharePasswordNotSupportedYet = "document.host.share.password_not_supported_yet";

    /// <summary>分享保护密码长度不符合最小或最大安全限制。</summary>
    public const string SharePasswordInvalidLength = "document.host_share.password_invalid_length";

    /// <summary>当前访问者身份不满足该 Host 分享链接的访问权限。</summary>
    public const string HostShareAccessDenied = "document.host_share.access_denied";

    /// <summary>访问该 Host 分享链接必须提供密码校验（语义同 SharePasswordRequired）。</summary>
    public const string HostSharePasswordRequired = SharePasswordRequired;

    /// <summary>授权操作引用的目标文档不存在，无法授予权限。</summary>
    public const string PermissionDocumentNotFound = "document.host_permission.document_not_found";

    /// <summary>已发布的全部 Document 错误码集合。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Invalid,
        NotFound,
        VersionConflict,
        InvalidFileReference,
        NoCurrentVersion,
        PreviewNotSupported,
        CategoryInvalid,
        CategoryNotFound,
        CategoryNameExists,
        CategoryInvalidParent,
        CategoryVersionConflict,
        CategoryHasChildren,
        CategoryInUse,
        TagInvalid,
        TagNotFound,
        TagNameExists,
        TagVersionConflict,
        TagInUse,
        ShareNotFound,
        ShareCodeExists,
        ShareInvalid,
        PermissionInvalid,
        RecycleItemNotFound,
        RecyclePurgeFailed,
        ShareVersionConflict,
        ShareCodeNotFound,
        ShareExpired,
        ShareDisabled,
        ShareMaxAccessReached,
        SharePasswordRequired,
        SharePasswordNotSupportedYet,
        SharePasswordInvalidLength,
        HostShareAccessDenied,
        PermissionDocumentNotFound,
    ]);
}
