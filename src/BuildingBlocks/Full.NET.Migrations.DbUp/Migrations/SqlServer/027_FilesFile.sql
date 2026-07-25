-- 027：Host 作用域文件元数据与本地存储索引。

IF OBJECT_ID(N'dbo.fn_files_file', N'U') IS NULL

BEGIN

    CREATE TABLE dbo.fn_files_file

    (

        Id uniqueidentifier NOT NULL,

        TenantId uniqueidentifier NULL,

        OriginalFileName nvarchar(260) NOT NULL,

        ContentType varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,

        SizeBytes bigint NOT NULL,

        StorageKey varchar(512) COLLATE Latin1_General_100_BIN2 NOT NULL,

        ContentHash char(64) NULL,

        CreatedAtUtc datetimeoffset(7) NOT NULL,

        CreatedByUserId uniqueidentifier NOT NULL,

        DeletedAtUtc datetimeoffset(7) NULL,

        CONSTRAINT PK_fn_files_file PRIMARY KEY CLUSTERED (Id)

    );

    CREATE INDEX IX_fn_files_file_CreatedAtUtc

        ON dbo.fn_files_file(CreatedAtUtc DESC, Id)

        WHERE DeletedAtUtc IS NULL;

    CREATE UNIQUE INDEX UX_fn_files_file_StorageKey

        ON dbo.fn_files_file(StorageKey)

        WHERE DeletedAtUtc IS NULL;

END;

