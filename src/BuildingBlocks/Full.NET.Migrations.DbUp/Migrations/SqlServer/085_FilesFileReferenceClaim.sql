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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件文件引用声明表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'ConfirmedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ConfirmedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'ConsumerModule', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费方模块', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ConsumerModule';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'ConsumerReferenceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费方引用标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ConsumerReferenceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'FileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'FileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'IdempotencyKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'ReleasedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'释放时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'ReleasedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'SizeBytes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'大小(字节)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'SizeBytes';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'State', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'State';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_files_file_reference_claim')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_files_file_reference_claim'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_files_file_reference_claim', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';

    CREATE INDEX IX_fn_files_file_reference_claim_FileId_State
        ON dbo.fn_files_file_reference_claim (FileId, State);

    CREATE INDEX IX_fn_files_file_reference_claim_State_UpdatedAtUtc
        ON dbo.fn_files_file_reference_claim (State, UpdatedAtUtc, Id);
END;
