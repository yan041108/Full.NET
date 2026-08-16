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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件文件引用声明表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ConfirmedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费方模块', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ConsumerModule';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费方引用标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ConsumerReferenceId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ContentHash';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'FileId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'释放时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ReleasedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'大小(字节)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'SizeBytes';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'State';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';

    CREATE INDEX IX_fn_files_file_reference_claim_FileId_State
        ON dbo.fn_files_file_reference_claim (FileId, State);

    CREATE INDEX IX_fn_files_file_reference_claim_State_UpdatedAtUtc
        ON dbo.fn_files_file_reference_claim (State, UpdatedAtUtc, Id);
END;
