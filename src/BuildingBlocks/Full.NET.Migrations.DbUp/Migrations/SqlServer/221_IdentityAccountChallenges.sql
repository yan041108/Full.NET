-- 221：Identity 账号操作挑战；高写入表使用非聚集主键与时间聚集索引。

IF OBJECT_ID(N'dbo.fn_identity_account_challenge', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_account_challenge
    (
        ChallengeId uniqueidentifier NOT NULL,
        Purpose tinyint NOT NULL,
        NormalizedEmail nvarchar(320) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CredentialHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        ConsumedAtUtc datetimeoffset(7) NULL,
        AttemptCount int NOT NULL
            CONSTRAINT DF_fn_identity_account_challenge_AttemptCount DEFAULT (0),
        MaxAttempts int NOT NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_identity_account_challenge_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_account_challenge PRIMARY KEY NONCLUSTERED (ChallengeId),
        CONSTRAINT CK_fn_identity_account_challenge_CredentialHash
            CHECK (LEN(CredentialHash) = 64),
        CONSTRAINT CK_fn_identity_account_challenge_MaxAttempts
            CHECK (MaxAttempts BETWEEN 1 AND 20)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证account challenge表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'AttemptCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'尝试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'AttemptCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'ChallengeId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Challenge标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'ChallengeId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'ConsumedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'ConsumedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'CredentialHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Credential Hash', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'CredentialHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'MaxAttempts', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Max Attempts', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'MaxAttempts';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'NormalizedEmail', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Normalized Email', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'NormalizedEmail';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'Purpose', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用途', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'Purpose';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证账号操作挑战表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
      AND indexObject.name = N'IX_fn_identity_account_challenge_Purpose_NormalizedEmai_e46531a5'
)
    CREATE CLUSTERED INDEX IX_fn_identity_account_challenge_Purpose_NormalizedEmai_e46531a5
        ON dbo.fn_identity_account_challenge(Purpose, NormalizedEmail, CreatedAtUtc DESC, ChallengeId);
