CREATE TABLE IF NOT EXISTS fn_files_file_reference_claim (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    IdempotencyKey varchar(128) NOT NULL COMMENT '幂等键',
    FileId BINARY(16) NOT NULL COMMENT '文件标识',
    ConsumerModule varchar(64) NOT NULL COMMENT '消费方模块',
    ConsumerReferenceId BINARY(16) NOT NULL COMMENT '消费方引用标识',
    State varchar(16) NOT NULL COMMENT '状态',
    ContentHash varchar(128) NULL COMMENT '内容哈希',
    SizeBytes bigint NOT NULL COMMENT '大小(字节)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    ConfirmedAtUtc datetime(6) NULL COMMENT '确认时间(UTC)',
    ReleasedAtUtc datetime(6) NULL COMMENT '释放时间(UTC)',
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_fn_files_file_reference_claim_IdempotencyKey (IdempotencyKey),
    KEY IX_fn_files_file_reference_claim_FileId_State (FileId, State),
    KEY IX_fn_files_file_reference_claim_State_UpdatedAtUtc (State, UpdatedAtUtc, Id),
    CONSTRAINT FK_fn_files_file_reference_claim_File
        FOREIGN KEY (FileId) REFERENCES fn_files_file (Id)
) COMMENT='文件文件引用声明表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
