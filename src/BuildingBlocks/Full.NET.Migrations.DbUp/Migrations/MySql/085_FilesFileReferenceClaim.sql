CREATE TABLE IF NOT EXISTS fn_files_file_reference_claim
(
    Id BINARY(16) NOT NULL,
    IdempotencyKey varchar(128) NOT NULL,
    FileId BINARY(16) NOT NULL,
    ConsumerModule varchar(64) NOT NULL,
    ConsumerReferenceId BINARY(16) NOT NULL,
    State varchar(16) NOT NULL,
    ContentHash varchar(128) NULL,
    SizeBytes bigint NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NOT NULL,
    ConfirmedAtUtc datetime(6) NULL,
    ReleasedAtUtc datetime(6) NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_fn_files_file_reference_claim_IdempotencyKey (IdempotencyKey),
    KEY IX_fn_files_file_reference_claim_FileId_State (FileId, State),
    KEY IX_fn_files_file_reference_claim_State_UpdatedAtUtc (State, UpdatedAtUtc, Id),
    CONSTRAINT FK_fn_files_file_reference_claim_File
        FOREIGN KEY (FileId) REFERENCES fn_files_file (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
