namespace Full.NET.Modules.Files.Contracts;

/// <summary>
/// Host 作用域文件元数据 API 的稳定权限码目录。
/// </summary>
/// <remarks>
/// 机器码稳定性：发布后所有权限码字符串保持不变，新增只能追加；
/// 授权目录、角色授权与前端路由守卫均按精确匹配校验。
/// </remarks>
public static class HostFilePermissions
{
    /// <summary>分页查询 Host 文件列表与元数据详情。</summary>
    public const string Read = "files.files.read";

    /// <summary>上传 Host 作用域文件并写入引用声明（Claim）。</summary>
    public const string Upload = "files.files.upload";

    /// <summary>下载 Host 文件内容；下载前会校验访问者的文件引用声明权限。</summary>
    public const string Download = "files.files.download";

    /// <summary>软删除 Host 文件；存在未释放引用时拒绝删除。</summary>
    public const string Delete = "files.files.delete";
}

/// <summary>Host 文件元数据列表项与详情响应。</summary>
/// <param name="Id">Host 文件稳定标识。</param>
/// <param name="OriginalFileName">上传时客户端提交的原始文件名，仅用于展示。</param>
/// <param name="ContentType">HTTP Content-Type（MIME 类型）；用于下载响应头。</param>
/// <param name="SizeBytes">文件字节数；用于配额计算与展示。</param>
/// <param name="ContentHash">文件内容哈希（算法由存储后端决定）；用于去重检测与完整性校验，缺失时为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">文件上传完成时间（UTC）。</param>
/// <param name="CreatedByUserId">上传者 Host 用户标识；用于审计与配额归属。</param>
public sealed record HostFileResponse(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string? ContentHash,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId);
