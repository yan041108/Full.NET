-- 027：Host 作用域文件元数据与本地存储索引。

CREATE TABLE IF NOT EXISTS fn_files_file

(

    Id BINARY(16) NOT NULL,

    TenantId BINARY(16) NULL,

    OriginalFileName varchar(260) NOT NULL,

    ContentType varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,

    SizeBytes bigint NOT NULL,

    StorageKey varchar(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,

    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NULL,

    CreatedAtUtc datetime(6) NOT NULL,

    CreatedByUserId BINARY(16) NOT NULL,

    DeletedAtUtc datetime(6) NULL,

    CONSTRAINT PK_fn_files_file PRIMARY KEY (Id),

    KEY IX_fn_files_file_CreatedAtUtc (CreatedAtUtc, Id),

    UNIQUE KEY UX_fn_files_file_StorageKey (StorageKey)

) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

