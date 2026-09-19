-- 222：注册策略三态模式与注册邀请表。

IF COL_LENGTH(N'dbo.fn_identity_registration_policy', N'RegistrationMode') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_registration_policy
        ADD RegistrationMode tinyint NOT NULL
            CONSTRAINT DF_fn_identity_registration_policy_RegistrationMode DEFAULT (1);
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_policy'), N'RegistrationMode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Registration Mode', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_policy', @level2type=N'COLUMN', @level2name=N'RegistrationMode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_policy')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_policy'), N'RegistrationMode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'注册模式：0=禁用，1=仅邀请，2=开放', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_policy', @level2type=N'COLUMN', @level2name=N'RegistrationMode';
END;

-- 分批编译回填语句，避免首次迁移时尚不能绑定新增列。
GO

UPDATE dbo.fn_identity_registration_policy
SET RegistrationMode = CASE WHEN IsPublicRegistrationEnabled = 1 THEN 2 ELSE 1 END
WHERE RegistrationMode = 1
  AND IsPublicRegistrationEnabled = 1;

IF OBJECT_ID(N'dbo.fn_identity_registration_invitation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_registration_invitation
    (
        InvitationId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        NormalizedEmail nvarchar(320) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CredentialHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RegistrationWayId uniqueidentifier NOT NULL,
        Status tinyint NOT NULL
            CONSTRAINT DF_fn_identity_registration_invitation_Status DEFAULT (0),
        BoundUserId uniqueidentifier NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        ConsumedAtUtc datetimeoffset(7) NULL,
        RevokedAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_identity_registration_invitation_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_registration_invitation PRIMARY KEY NONCLUSTERED (InvitationId),
        CONSTRAINT CK_fn_identity_registration_invitation_CredentialHash
            CHECK (LEN(CredentialHash) = 64),
        CONSTRAINT FK_fn_identity_registration_invitation_RegistrationWay
            FOREIGN KEY (RegistrationWayId) REFERENCES dbo.fn_identity_user_registration_way (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证registration invitation表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'BoundUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Bound User标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'BoundUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'ConsumedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'ConsumedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'CredentialHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Credential Hash', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'CredentialHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'InvitationId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Invitation标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'InvitationId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'NormalizedEmail', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Normalized Email', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'NormalizedEmail';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'RegistrationWayId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Registration Way标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'RegistrationWayId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'RevokedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'撤销时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'RevokedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_registration_invitation'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证注册邀请表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_registration_invitation';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_registration_invitation')
      AND indexObject.name = N'IX_fn_identity_registration_invitation_TenantId_Normali_6d9152b8'
)
    CREATE CLUSTERED INDEX IX_fn_identity_registration_invitation_TenantId_Normali_6d9152b8
        ON dbo.fn_identity_registration_invitation(TenantId, NormalizedEmail, Status, CreatedAtUtc DESC, InvitationId);
