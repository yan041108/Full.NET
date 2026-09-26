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

    /// <summary>列出所属资源全部就绪文件，供任务在上传成功后崩溃时绑定已有对象。</summary>
    public static readonly SqlStatement ListReady = new("files.tenant_resource_file.list_ready", """
        SELECT Id, OriginalFileName, CreatedAtUtc
        FROM fn_files_tenant_resource_file
        WHERE TenantId = @TenantId AND OwnerModuleKey = @OwnerModuleKey AND ResourceId = @ResourceId
          AND StatusKey = 'ready'
        ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>Host 运维对账：按显式 TenantId 汇总 ready 资源文件字节数。</summary>
    public static readonly SqlStatement SumReadyBytesByTenantForHost = new(
        "files.tenant_resource_file.sum_ready_bytes_by_tenant_host",
        """
        SELECT COALESCE(SUM(SizeBytes), 0)
        FROM fn_files_tenant_resource_file
        WHERE TenantId = @TenantId
          AND StatusKey = 'ready'
        """,
        SqlDataScope.HostOnly);

    /// <summary>Worker 目录：陈旧 pending/ready 文件，具体操作必须在租户上下文中执行。</summary>
    public static readonly SqlStatement SelectStaleSqlServer = new(
        "files.tenant_resource_file.select_stale.sql_server",
        """
        SELECT TOP (@BatchSize)
               Id, TenantId, OwnerModuleKey, ResourceId, ProviderKey, StorageKey, StatusKey, CreatedAtUtc
        FROM fn_files_tenant_resource_file
        WHERE StatusKey IN ('pending', 'ready', 'released')
          AND CreatedAtUtc <= @CreatedBeforeUtc
          AND (@HasCursor = 0
               OR CreatedAtUtc > @AfterCreatedAtUtc
               OR (CreatedAtUtc = @AfterCreatedAtUtc AND Id > @AfterId))
        ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.Global);

    /// <summary>MySQL Worker 陈旧租户资源文件目录。</summary>
    public static readonly SqlStatement SelectStaleMySql = new(
        "files.tenant_resource_file.select_stale.mysql",
        """
        SELECT Id, TenantId, OwnerModuleKey, ResourceId, ProviderKey, StorageKey, StatusKey, CreatedAtUtc
        FROM fn_files_tenant_resource_file
        WHERE StatusKey IN ('pending', 'ready', 'released')
          AND CreatedAtUtc <= @CreatedBeforeUtc
          AND (@HasCursor = 0
               OR CreatedAtUtc > @AfterCreatedAtUtc
               OR (CreatedAtUtc = @AfterCreatedAtUtc AND Id > @AfterId))
        ORDER BY CreatedAtUtc, Id
        LIMIT @BatchSize
        """, SqlDataScope.Global);

    /// <summary>对象存在时把陈旧 pending 提升为可读。</summary>
    public static readonly SqlStatement PromotePending = new("files.tenant_resource_file.promote_pending", """
        UPDATE fn_files_tenant_resource_file SET StatusKey = 'ready'
        WHERE TenantId = @TenantId AND Id = @Id AND OwnerModuleKey = @OwnerModuleKey
          AND ResourceId = @ResourceId AND StatusKey = 'pending'
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>对象不存在时删除从未发布的 pending 元数据。</summary>
    public static readonly SqlStatement PurgePending = new("files.tenant_resource_file.purge_pending", """
        DELETE FROM fn_files_tenant_resource_file
        WHERE TenantId = @TenantId AND Id = @Id AND OwnerModuleKey = @OwnerModuleKey
          AND ResourceId = @ResourceId AND StatusKey = 'pending'
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
    /// <summary>物理删除成功后移除释放墓碑，失败时必须保留供下轮重试。</summary>
    public static readonly SqlStatement PurgeReleased = new("files.tenant_resource_file.purge_released", """
        DELETE FROM fn_files_tenant_resource_file
        WHERE TenantId = @TenantId AND Id = @Id AND OwnerModuleKey = @OwnerModuleKey
          AND ResourceId = @ResourceId AND StatusKey = 'released'
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

/// <summary>所属资源就绪文件的内部投影。</summary>
/// <param name="Id">文件标识。</param>
/// <param name="OriginalFileName">下载名。</param>
/// <param name="CreatedAtUtc">创建时间 UTC。</param>
internal sealed record TenantResourceFileReadyRecord(Guid Id, string OriginalFileName, DateTimeOffset CreatedAtUtc);

/// <summary>租户资源文件对账扫描行。</summary>
/// <param name="Id">文件标识。</param>
/// <param name="TenantId">所属租户。</param>
/// <param name="OwnerModuleKey">所属模块。</param>
/// <param name="ResourceId">所属资源。</param>
/// <param name="ProviderKey">存储提供程序。</param>
/// <param name="StorageKey">对象键。</param>
/// <param name="StatusKey">当前状态。</param>
/// <param name="CreatedAtUtc">创建时间 UTC。</param>
internal sealed record TenantResourceFileReconciliationRecord(
    Guid Id,
    Guid TenantId,
    string OwnerModuleKey,
    Guid ResourceId,
    string ProviderKey,
    string StorageKey,
    string StatusKey,
    DateTimeOffset CreatedAtUtc);
