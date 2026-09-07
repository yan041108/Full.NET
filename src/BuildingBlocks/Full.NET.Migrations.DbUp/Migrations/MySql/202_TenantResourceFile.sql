-- 202：租户资源文件；UUID 使用标准 Binary16，禁止跨模块外键。
CREATE TABLE IF NOT EXISTS fn_files_tenant_resource_file (
    Id binary(16) NOT NULL COMMENT '文件 UUID v7 标识',
    TenantId binary(16) NOT NULL COMMENT '所属租户',
    OwnerModuleKey varchar(64) NOT NULL COMMENT '资源所属模块稳定键',
    ResourceId binary(16) NOT NULL COMMENT '所属业务资源标识',
    OriginalFileName varchar(255) NOT NULL COMMENT '安全下载文件名',
    ContentType varchar(128) NOT NULL COMMENT '内容类型',
    SizeBytes bigint NOT NULL COMMENT '实际文件字节数',
    ContentHash varchar(64) NOT NULL COMMENT '实际内容 SHA-256 摘要',
    ProviderKey varchar(64) NOT NULL COMMENT '存储提供程序稳定键',
    StorageKey varchar(256) NOT NULL COMMENT 'Files 生成的不可外传对象键',
    StatusKey varchar(16) NOT NULL COMMENT '上传发布及释放状态',
    CreatedByUserId binary(16) NOT NULL COMMENT '已授权创建主体',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间 UTC',
    CONSTRAINT PK_fn_files_tenant_resource_file PRIMARY KEY (Id),
    CONSTRAINT CK_fn_files_tenant_resource_file_StatusKey CHECK (StatusKey IN ('pending', 'ready', 'released'))
) ENGINE=InnoDB COMMENT='租户业务资源文件所有权与上传生命周期';
