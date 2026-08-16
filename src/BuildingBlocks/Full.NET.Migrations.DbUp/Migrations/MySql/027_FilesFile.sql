-- 027：Host 作用域文件元数据与本地存储索引。

CREATE TABLE IF NOT EXISTS fn_files_file (

    Id BINARY(16) NOT NULL COMMENT '逻辑主键',

    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',

    OriginalFileName varchar(260) NOT NULL COMMENT '原始文件名',

    ContentType varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容类型',

    SizeBytes bigint NOT NULL COMMENT '大小(字节)',

    StorageKey varchar(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '存储键',

    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '内容哈希',

    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',

    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',

    DeletedAtUtc datetime(6) NULL COMMENT '删除时间(UTC)',

    CONSTRAINT PK_fn_files_file PRIMARY KEY (Id),

    KEY IX_fn_files_file_CreatedAtUtc (CreatedAtUtc, Id),

    UNIQUE KEY UX_fn_files_file_StorageKey (StorageKey)

) COMMENT='文件文件表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

