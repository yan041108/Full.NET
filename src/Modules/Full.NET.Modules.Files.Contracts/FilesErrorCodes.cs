namespace Full.NET.Modules.Files.Contracts;

/// <summary>
/// Files 模块对外返回的稳定错误码目录。
/// </summary>
/// <remarks>
/// 机器码稳定性：发布后所有错误码字符串保持不变，新增只能追加；
/// 消费方应按前缀匹配或精确比较，不得依赖错误码的字典序或数值派生含义。
/// </remarks>
public static class FilesErrorCodes
{
    /// <summary>Files 模块所有错误码的统一前缀。</summary>
    public const string Prefix = "files.";

    /// <summary>目标文件元数据不存在或已软删除。</summary>
    public const string FileNotFound = "files.file.not_found";

    /// <summary>上传请求内容、表单结构或分片参数校验失败。</summary>
    public const string InvalidUpload = "files.file.invalid_upload";

    /// <summary>上传文件字节数超出当前租户或套餐允许的单文件上限。</summary>
    public const string FileTooLarge = "files.file.too_large";

    /// <summary>文件引用声明（Claim）请求缺少业务键、过期时间或签名校验失败。</summary>
    public const string InvalidClaim = "files.file_reference_claim.invalid";

    /// <summary>目标文件引用声明不存在、已释放或属于其他租户。</summary>
    public const string ClaimNotFound = "files.file_reference_claim.not_found";

    /// <summary>同一业务键下提交的引用声明负载与已登记声明冲突，禁止重复登记。</summary>
    public const string ClaimPayloadConflict = "files.file_reference_claim.payload_conflict";

    /// <summary>目标文件仍存在未释放的引用声明，无法执行软删除或物理清理。</summary>
    public const string FileReferenced = "files.file.referenced";

    /// <summary>已发布的全部 Files 错误码集合。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        FileNotFound,
        InvalidUpload,
        FileTooLarge,
        InvalidClaim,
        ClaimNotFound,
        ClaimPayloadConflict,
        FileReferenced,
    ]);
}