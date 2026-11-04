-- 为 Identity 头像/签名及 Tenancy 标志展开绑定代际键；空值保留历史引用，不回填猜测的键。
-- SQL Server 以 COL_LENGTH 检测中断后的加列步骤；回退需先停写并保留代际数据，不能直接删列。
IF COL_LENGTH('dbo.fn_identity_user_profile', 'AvatarClaimKey') IS NULL
    ALTER TABLE dbo.fn_identity_user_profile ADD AvatarClaimKey nvarchar(200) NULL;
IF COL_LENGTH('dbo.fn_identity_user_profile', 'SignatureClaimKey') IS NULL
    ALTER TABLE dbo.fn_identity_user_profile ADD SignatureClaimKey nvarchar(200) NULL;
IF COL_LENGTH('dbo.fn_tenancy_tenant', 'LogoClaimKey') IS NULL
    ALTER TABLE dbo.fn_tenancy_tenant ADD LogoClaimKey nvarchar(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'AvatarClaimKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'头像绑定代际引用键，空值兼容历史绑定',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'AvatarClaimKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'SignatureClaimKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'签名绑定代际引用键，空值兼容历史绑定',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'SignatureClaimKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'LogoClaimKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标志绑定代际引用键，空值兼容历史绑定',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'LogoClaimKey';
