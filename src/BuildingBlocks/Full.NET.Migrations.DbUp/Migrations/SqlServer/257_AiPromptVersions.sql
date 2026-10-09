-- 257：新增 AI 自有的不可变文本 Prompt 版本与发布指针，不自动发布、收费或调用模型。
-- 应用回退保留新表；MySQL DDL 隐式提交，按元数据探测逐项恢复约束和注释；两个方向外键均不级联删除。
IF OBJECT_ID('dbo.fn_ai_prompt','U') IS NULL
BEGIN
CREATE TABLE dbo.fn_ai_prompt (
    Id uniqueidentifier NOT NULL,
    ScopeKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    TenantId uniqueidentifier NULL,
    OwnerUserId uniqueidentifier NOT NULL,
    ClientRequestId uniqueidentifier NOT NULL,
    CreateHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Name nvarchar(128) NOT NULL,
    Revision int NOT NULL,
    LastVersion int NOT NULL,
    ActiveVersionId uniqueidentifier NULL,
    Epoch int NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT PK_fn_ai_prompt PRIMARY KEY CLUSTERED (Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前作用域的 Prompt 目录与发布指针；停用代次撤销旧运行',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'当前作用域的 Prompt 目录与发布指针；停用代次撤销旧运行',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目录逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'Id';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'目录逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'ScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信宿主 host 或租户 UUID 规范串',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'ScopeKey';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'可信宿主 host 或租户 UUID 规范串',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'ScopeKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'TenantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户；宿主为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'TenantId';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'可信租户；宿主为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'OwnerUserId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目录创建者；不构成额外授权',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'OwnerUserId';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'目录创建者；不构成额外授权',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'OwnerUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'ClientRequestId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建者在作用域内的幂等键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'ClientRequestId';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'创建者在作用域内的幂等键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'ClientRequestId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'CreateHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'首版请求的规范载荷摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'CreateHash';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'首版请求的规范载荷摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'CreateHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'Name','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目录显示名称',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'Name';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'目录显示名称',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'Name';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'Revision','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目录并发修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'Revision';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'目录并发修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'Revision';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'LastVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最后追加的不可变版本号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'LastVersion';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'最后追加的不可变版本号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'LastVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'ActiveVersionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'显式发布版本；停用或未发布时为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'ActiveVersionId';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'显式发布版本；停用或未发布时为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'ActiveVersionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'Epoch','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'停用撤销代次；重新发布不会恢复旧代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'Epoch';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'停用撤销代次；重新发布不会恢复旧代次',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'Epoch';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目录创建 UTC 时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'目录创建 UTC 时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF OBJECT_ID('dbo.fn_ai_prompt_version','U') IS NULL
BEGIN
CREATE TABLE dbo.fn_ai_prompt_version (
    Id uniqueidentifier NOT NULL,
    PromptId uniqueidentifier NOT NULL,
    ScopeKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    TenantId uniqueidentifier NULL,
    Version int NOT NULL,
    FormatVersion int NOT NULL,
    BodyJson nvarchar(max) NOT NULL,
    ModelConfigVersion int NOT NULL,
    ProviderKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ModelId nvarchar(256) NOT NULL,
    BodyHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    WasPublished bit NOT NULL,
    CreatedByUserId uniqueidentifier NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT PK_fn_ai_prompt_version PRIMARY KEY CLUSTERED (Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=0 AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变纯文本 Prompt 正文与模型身份；首次发布标记不改写正文',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'不可变纯文本 Prompt 正文与模型身份；首次发布标记不改写正文',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'Id','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'不可变版本逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'Id';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'不可变版本逻辑主键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'PromptId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'所属 AI 目录',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'PromptId';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'所属 AI 目录',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'PromptId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'ScopeKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'与所属目录相同的可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'ScopeKey';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'与所属目录相同的可信作用域',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'ScopeKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'TenantId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户；宿主为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'TenantId';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'可信租户；宿主为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'Version','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目录内不可变版本号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'Version';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'目录内不可变版本号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'Version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'FormatVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'闭合纯文本策略协议版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'FormatVersion';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'闭合纯文本策略协议版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'FormatVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'BodyJson','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'源生成规范正文；固定文本 Schema、零工具及无检索',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'BodyJson';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'源生成规范正文；固定文本 Schema、零工具及无检索',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'BodyJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'ModelConfigVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'草稿创建时绑定的模型配置修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'ModelConfigVersion';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'草稿创建时绑定的模型配置修订号',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'ModelConfigVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'ProviderKey','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定的提供程序稳定键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'ProviderKey';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'绑定的提供程序稳定键',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'ProviderKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'ModelId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定的模型稳定标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'ModelId';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'绑定的模型稳定标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'ModelId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'BodyHash','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'完整规范正文和模型身份的 SHA256 摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'BodyHash';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'完整规范正文和模型身份的 SHA256 摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'BodyHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'WasPublished','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'是否曾经显式发布；不因停用清除',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'WasPublished';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'是否曾经显式发布；不因停用清除',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'WasPublished';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'CreatedByUserId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'版本创建者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'CreatedByUserId';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'版本创建者',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'CreatedByUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt_version'),'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'版本创建 UTC 时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
ELSE
 EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'版本创建 UTC 时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt_version',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_ai_prompt') AND name='UX_fn_ai_prompt_ScopeKey_OwnerUserId_ClientRequestId')
 CREATE UNIQUE INDEX UX_fn_ai_prompt_ScopeKey_OwnerUserId_ClientRequestId ON fn_ai_prompt (ScopeKey,OwnerUserId,ClientRequestId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_ai_prompt') AND name='UX_fn_ai_prompt_ScopeKey_Id')
 CREATE UNIQUE INDEX UX_fn_ai_prompt_ScopeKey_Id ON fn_ai_prompt (ScopeKey,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_ai_prompt') AND name='IX_fn_ai_prompt_ScopeKey_CreatedAtUtc_Id')
 CREATE INDEX IX_fn_ai_prompt_ScopeKey_CreatedAtUtc_Id ON fn_ai_prompt (ScopeKey,CreatedAtUtc DESC,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND name='UX_fn_ai_prompt_version_PromptId_Version')
 CREATE UNIQUE INDEX UX_fn_ai_prompt_version_PromptId_Version ON fn_ai_prompt_version (PromptId,Version);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND name='UX_fn_ai_prompt_version_ScopeKey_PromptId_Id')
 CREATE UNIQUE INDEX UX_fn_ai_prompt_version_ScopeKey_PromptId_Id ON fn_ai_prompt_version (ScopeKey,PromptId,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND name='FK_fn_ai_prompt_version_ScopeKey_PromptId')
 ALTER TABLE fn_ai_prompt_version ADD CONSTRAINT FK_fn_ai_prompt_version_ScopeKey_PromptId FOREIGN KEY (ScopeKey,PromptId) REFERENCES fn_ai_prompt (ScopeKey,Id);
GO
ALTER TABLE fn_ai_prompt_version WITH CHECK CHECK CONSTRAINT FK_fn_ai_prompt_version_ScopeKey_PromptId;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_prompt') AND name='FK_fn_ai_prompt_ScopeKey_Id_ActiveVersionId')
 ALTER TABLE fn_ai_prompt ADD CONSTRAINT FK_fn_ai_prompt_ScopeKey_Id_ActiveVersionId FOREIGN KEY (ScopeKey,Id,ActiveVersionId) REFERENCES fn_ai_prompt_version (ScopeKey,PromptId,Id);
GO
ALTER TABLE fn_ai_prompt WITH CHECK CHECK CONSTRAINT FK_fn_ai_prompt_ScopeKey_Id_ActiveVersionId;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_prompt') AND name='CK_fn_ai_prompt_State')
 ALTER TABLE fn_ai_prompt ADD CONSTRAINT CK_fn_ai_prompt_State CHECK (Revision>=1 AND LastVersion>=1 AND Revision>=LastVersion AND Epoch>=0 AND ((TenantId IS NULL AND ScopeKey='host' AND DATALENGTH(ScopeKey)=4) OR (TenantId IS NOT NULL AND ScopeKey=LOWER(REPLACE(CONVERT(varchar(36),TenantId),'-','')) AND DATALENGTH(ScopeKey)=32)) AND (DATALENGTH(CreateHash)=64 AND CreateHash NOT LIKE '%[^0-9a-f]%'));
GO
ALTER TABLE fn_ai_prompt WITH CHECK CHECK CONSTRAINT CK_fn_ai_prompt_State;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.fn_ai_prompt_version') AND name='CK_fn_ai_prompt_version_State')
 ALTER TABLE fn_ai_prompt_version ADD CONSTRAINT CK_fn_ai_prompt_version_State CHECK (Version>=1 AND FormatVersion=1 AND ModelConfigVersion>=1 AND ((TenantId IS NULL AND ScopeKey='host' AND DATALENGTH(ScopeKey)=4) OR (TenantId IS NOT NULL AND ScopeKey=LOWER(REPLACE(CONVERT(varchar(36),TenantId),'-','')) AND DATALENGTH(ScopeKey)=32)) AND (DATALENGTH(BodyHash)=64 AND BodyHash NOT LIKE '%[^0-9a-f]%') AND ISJSON(BodyJson)=1);
GO
ALTER TABLE fn_ai_prompt_version WITH CHECK CHECK CONSTRAINT CK_fn_ai_prompt_version_State;
GO
