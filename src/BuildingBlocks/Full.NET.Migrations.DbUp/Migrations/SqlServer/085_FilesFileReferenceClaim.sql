-- 085：Files 模块维护的跨模块文件引用 claim 状态表。

IF OBJECT_ID(N'dbo.fn_files_file_reference_claim', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_files_file_reference_claim
    (
        Id uniqueidentifier NOT NULL,
        IdempotencyKey nvarchar(128) NOT NULL,
        FileId uniqueidentifier NOT NULL,
        ConsumerModule nvarchar(64) NOT NULL,
        ConsumerReferenceId uniqueidentifier NOT NULL,
        State nvarchar(16) NOT NULL,
        ContentHash nvarchar(128) NULL,
        SizeBytes bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        ConfirmedAtUtc datetimeoffset(7) NULL,
        ReleasedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_files_file_reference_claim PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_fn_files_file_reference_claim_IdempotencyKey UNIQUE (IdempotencyKey),
        CONSTRAINT FK_fn_files_file_reference_claim_File
            FOREIGN KEY (FileId) REFERENCES dbo.fn_files_file (Id)
    );

    CREATE INDEX IX_fn_files_file_reference_claim_FileId_State
        ON dbo.fn_files_file_reference_claim (FileId, State);

    CREATE INDEX IX_fn_files_file_reference_claim_State_UpdatedAtUtc
        ON dbo.fn_files_file_reference_claim (State, UpdatedAtUtc, Id);
END;
