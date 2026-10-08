namespace Full.NET.Modules.Files.Contracts;

/// <summary>可选的上传保护端口；Files 在对象写入前通知所属模块持久化精确文件意图。</summary>
/// <remarks>调用发生在所有数据库事务之外。实现须在返回前提交意图，并通过 IsReferencedAsync 原子仲裁过期清理与最终绑定；不以时间宽限代替仲裁。</remarks>
public interface ITenantResourceFileUploadOwner : ITenantResourceFileOwner
{
    /// <summary>记录受信 Files 生成的文件身份与实际内容元数据；失败时不得继续对象上传。</summary>
    Task BeginUploadAsync(TenantResourceFileUploadIntent intent, CancellationToken cancellationToken = default);
}

/// <summary>仅供同进程受控端口使用的已验证上传描述，不包含对象存储路径。</summary>
public sealed record TenantResourceFileUploadIntent(Guid ResourceId, Guid FileId, Guid ActorUserId,
    string OriginalFileName, long SizeBytes, DateTimeOffset CreatedAtUtc);
