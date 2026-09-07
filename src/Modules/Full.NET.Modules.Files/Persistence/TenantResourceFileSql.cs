using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Files.Persistence;

/// <summary>租户资源文件独立元数据；所有访问均绑定可信租户和精确所属资源。</summary>
internal static class TenantResourceFileSql
{
    /// <summary>只在资源所属模块已确认当前租户真实引用后读取存量 Host 对象；不提供按 UUID 的公共回退接口。</summary>
    public static readonly SqlStatement FindAuthorizedLegacyReference = new("files.tenant_resource_file.authorized_legacy_reference", """
        SELECT Id, OriginalFileName, ContentType, SizeBytes, COALESCE(ContentHash, '') AS ContentHash,
               ProviderKey, StorageKey, 'ready' AS StatusKey
        FROM fn_files_file
        WHERE Id = @Id AND TenantId IS NULL AND StorageState = 'ready' AND DeletedAtUtc IS NULL
        """, SqlDataScope.Global);

    /// <summary>登记尚不可读取的上传意图与所有权。</summary>
    public static readonly SqlStatement Insert = new("files.tenant_resource_file.insert", """
        INSERT INTO fn_files_tenant_resource_file
            (Id, TenantId, OwnerModuleKey, ResourceId, OriginalFileName, ContentType,
             SizeBytes, ContentHash, ProviderKey, StorageKey, StatusKey, CreatedByUserId, CreatedAtUtc)
        VALUES (@Id, @TenantId, @OwnerModuleKey, @ResourceId, @OriginalFileName, @ContentType,
            @SizeBytes, @ContentHash, @ProviderKey, @StorageKey, 'pending', @CreatedByUserId, @CreatedAtUtc)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>精确读取所属资源的文件，不根据文件 UUID 单独授予访问权。</summary>
    public static readonly SqlStatement FindOwned = new("files.tenant_resource_file.find_owned", """
        SELECT Id, OriginalFileName, ContentType, SizeBytes, ContentHash, ProviderKey, StorageKey, StatusKey
        FROM fn_files_tenant_resource_file
        WHERE TenantId = @TenantId AND Id = @Id AND OwnerModuleKey = @OwnerModuleKey AND ResourceId = @ResourceId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>只有持久化对象后才发布文件；已释放的意图不能复活。</summary>
    public static readonly SqlStatement MarkReady = new("files.tenant_resource_file.mark_ready", """
        UPDATE fn_files_tenant_resource_file SET StatusKey = 'ready'
        WHERE TenantId = @TenantId AND Id = @Id AND OwnerModuleKey = @OwnerModuleKey
          AND ResourceId = @ResourceId AND StatusKey = 'pending'
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>先撤销读取资格，再执行可重试的物理删除。</summary>
    public static readonly SqlStatement Release = new("files.tenant_resource_file.release", """
        UPDATE fn_files_tenant_resource_file SET StatusKey = 'released'
        WHERE TenantId = @TenantId AND Id = @Id AND OwnerModuleKey = @OwnerModuleKey AND ResourceId = @ResourceId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>文件内容定位仅在 Files 实现内部使用。</summary>
/// <param name="Id">文件标识。</param>
/// <param name="OriginalFileName">下载名。</param>
/// <param name="ContentType">内容类型。</param>
/// <param name="SizeBytes">实际大小。</param>
/// <param name="ContentHash">内容摘要。</param>
/// <param name="ProviderKey">存储提供程序。</param>
/// <param name="StorageKey">Files 生成的对象键。</param>
/// <param name="StatusKey">上传和释放状态。</param>
internal sealed record TenantResourceFileRecord(Guid Id, string OriginalFileName, string ContentType,
    long SizeBytes, string ContentHash, string ProviderKey, string StorageKey, string StatusKey);
