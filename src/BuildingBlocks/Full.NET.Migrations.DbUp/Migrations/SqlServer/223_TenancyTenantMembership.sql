-- 223: tenant lifecycle, membership and invitations.
IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'LifecycleStatus') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD LifecycleStatus nvarchar(32) NOT NULL
            CONSTRAINT DF_fn_tenancy_tenant_LifecycleStatus DEFAULT (N'Active');
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'LifecycleStatus', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Lifecycle Status', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'LifecycleStatus';
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'OwnerUserId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD OwnerUserId uniqueidentifier NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'OwnerUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所有者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'OwnerUserId';
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'ProvisioningStatus') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD ProvisioningStatus nvarchar(32) NOT NULL
            CONSTRAINT DF_fn_tenancy_tenant_ProvisioningStatus DEFAULT (N'Completed');
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'ProvisioningStatus', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Provisioning Status', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'ProvisioningStatus';
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'ProvisioningStep') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD ProvisioningStep nvarchar(64) NULL;
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'ProvisioningStep', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Provisioning Step', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'ProvisioningStep';
END;

-- 新增列必须先完成批次，再编译引用这些列的数据回填。
GO

UPDATE dbo.fn_tenancy_tenant
SET LifecycleStatus = N'Active'
WHERE LifecycleStatus IS NULL OR LTRIM(RTRIM(LifecycleStatus)) = N'';

UPDATE dbo.fn_tenancy_tenant
SET ProvisioningStatus = N'Completed'
WHERE ProvisioningStatus IS NULL OR LTRIM(RTRIM(ProvisioningStatus)) = N'';

IF OBJECT_ID(N'dbo.fn_identity_tenant_member', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_tenant_member (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        MemberRole nvarchar(32) NOT NULL,
        Status nvarchar(32) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_identity_tenant_member_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_tenant_member PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_member')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证tenant member表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_member';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_member')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_member'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_member', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_member')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_member'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_member', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_member')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_member'), N'MemberRole', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Member Role', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_member', @level2type=N'COLUMN', @level2name=N'MemberRole';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_member')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_member'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_member', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_member')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_member'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_member', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_member')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_member'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_member', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_member')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_member'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_member', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_member')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_member'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_member', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_identity_tenant_member_TenantUser' AND object_id = OBJECT_ID(N'dbo.fn_identity_tenant_member'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_identity_tenant_member_TenantUser
        ON dbo.fn_identity_tenant_member (TenantId, UserId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_tenant_member_TenantId' AND object_id = OBJECT_ID(N'dbo.fn_identity_tenant_member'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_tenant_member_TenantId
        ON dbo.fn_identity_tenant_member (TenantId);

IF OBJECT_ID(N'dbo.fn_identity_tenant_invitation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_tenant_invitation (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        TargetEmail nvarchar(320) NOT NULL,
        TargetUserId uniqueidentifier NULL,
        InvitedByUserId uniqueidentifier NOT NULL,
        MemberRole nvarchar(32) NOT NULL,
        TokenHash nvarchar(128) NOT NULL,
        Status nvarchar(32) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_identity_tenant_invitation_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_tenant_invitation PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证tenant invitation表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'InvitedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Invited By User标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'InvitedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'MemberRole', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Member Role', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'MemberRole';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'TargetEmail', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Target Email', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'TargetEmail';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'TargetUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Target User标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'TargetUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'TokenHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'令牌哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'TokenHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_tenant_invitation'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_tenant_invitation', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fn_identity_tenant_invitation_TenantEmail' AND object_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation'))
    CREATE NONCLUSTERED INDEX IX_fn_identity_tenant_invitation_TenantEmail
        ON dbo.fn_identity_tenant_invitation (TenantId, TargetEmail);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_fn_identity_tenant_invitation_TokenHash' AND object_id = OBJECT_ID(N'dbo.fn_identity_tenant_invitation'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_identity_tenant_invitation_TokenHash
        ON dbo.fn_identity_tenant_invitation (TokenHash);
