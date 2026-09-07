using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>租户业务资源的文件所有权合同；调用方先授权资源，Files 再核对租户、模块和资源标识。</summary>
/// <remarks>所有者模块键由受信模块代码提供，不接受客户端覆盖；外部存储操作不得嵌套业务事务。</remarks>
public interface ITenantResourceFileStore
{
    /// <summary>先持久化文件所有权，再上传对象；成功后才允许读取。</summary>
    /// <param name="ownerModuleKey">受信所属模块键。</param>
    /// <param name="resourceId">调用模块预先生成的资源标识。</param>
    /// <param name="actorUserId">已授权主体。</param>
    /// <param name="originalFileName">下载文件名，不作为物理路径。</param>
    /// <param name="contentType">下载内容类型。</param>
    /// <param name="content">待上传流。</param>
    /// <param name="contentLength">声明长度，实际读取仍受上限约束。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId,
        Guid actorUserId, string originalFileName, string contentType, Stream content, long contentLength,
        CancellationToken cancellationToken = default);

    /// <summary>仅打开当前租户、模块和资源共同拥有的就绪文件。</summary>
    /// <param name="ownerModuleKey">受信所属模块键。</param>
    /// <param name="resourceId">已授权的资源标识。</param>
    /// <param name="fileId">文件引用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId,
        Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>撤销所有权可读状态并幂等删除对象；删除异常允许使用同一引用重试。</summary>
    /// <param name="ownerModuleKey">受信所属模块键。</param>
    /// <param name="resourceId">已授权的资源标识。</param>
    /// <param name="fileId">待释放的文件引用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>租户资源文件的不透明引用，不暴露存储位置。</summary>
/// <param name="FileId">文件标识。</param>
/// <param name="SizeBytes">实际上传字节数。</param>
/// <param name="ContentHash">实际内容摘要。</param>
public sealed record TenantResourceFileReference(Guid FileId, long SizeBytes, string ContentHash);

/// <summary>调用方负责释放的租户文件内容。</summary>
/// <param name="Content">只读内容流。</param>
/// <param name="ContentType">内容类型。</param>
/// <param name="OriginalFileName">下载文件名。</param>
public sealed record TenantResourceFileContent(Stream Content, string ContentType, string OriginalFileName);
