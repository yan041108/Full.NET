-- 257：新增 AI 自有的不可变文本 Prompt 版本与发布指针，不自动发布、收费或调用模型。
-- 应用回退保留新表；MySQL DDL 隐式提交，按元数据探测逐项恢复约束和注释；两个方向外键均不级联删除。
CREATE TABLE IF NOT EXISTS fn_ai_prompt (
    Id binary(16) NOT NULL COMMENT '目录逻辑主键',
    ScopeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信宿主 host 或租户 UUID 规范串',
    TenantId binary(16) NULL COMMENT '可信租户；宿主为空',
    OwnerUserId binary(16) NOT NULL COMMENT '目录创建者；不构成额外授权',
    ClientRequestId binary(16) NOT NULL COMMENT '创建者在作用域内的幂等键',
    CreateHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '首版请求的规范载荷摘要',
    Name varchar(128) NOT NULL COMMENT '目录显示名称',
    Revision int NOT NULL COMMENT '目录并发修订号',
    LastVersion int NOT NULL COMMENT '最后追加的不可变版本号',
    ActiveVersionId binary(16) NULL COMMENT '显式发布版本；停用或未发布时为空',
    Epoch int NOT NULL COMMENT '停用撤销代次；重新发布不会恢复旧代次',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '目录创建 UTC 时间',
    CONSTRAINT PK_fn_ai_prompt PRIMARY KEY (Id)
) COMMENT='当前作用域的 Prompt 目录与发布指针；停用代次撤销旧运行' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_ai_prompt_version (
    Id binary(16) NOT NULL COMMENT '不可变版本逻辑主键',
    PromptId binary(16) NOT NULL COMMENT '所属 AI 目录',
    ScopeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '与所属目录相同的可信作用域',
    TenantId binary(16) NULL COMMENT '可信租户；宿主为空',
    Version int NOT NULL COMMENT '目录内不可变版本号',
    FormatVersion int NOT NULL COMMENT '闭合纯文本策略协议版本',
    BodyJson longtext NOT NULL COMMENT '源生成规范正文；固定文本 Schema、零工具及无检索',
    ModelConfigVersion int NOT NULL COMMENT '草稿创建时绑定的模型配置修订号',
    ProviderKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '绑定的提供程序稳定键',
    ModelId varchar(256) NOT NULL COMMENT '绑定的模型稳定标识',
    BodyHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整规范正文和模型身份的 SHA256 摘要',
    WasPublished tinyint NOT NULL COMMENT '是否曾经显式发布；不因停用清除',
    CreatedByUserId binary(16) NOT NULL COMMENT '版本创建者',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '版本创建 UTC 时间',
    CONSTRAINT PK_fn_ai_prompt_version PRIMARY KEY (Id)
) COMMENT='不可变纯文本 Prompt 正文与模型身份；首次发布标记不改写正文' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
SET @FnAiPromptDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND INDEX_NAME='UX_fn_ai_prompt_ScopeKey_OwnerUserId_ClientRequestId'), 'CREATE UNIQUE INDEX UX_fn_ai_prompt_ScopeKey_OwnerUserId_ClientRequestId ON fn_ai_prompt (ScopeKey,OwnerUserId,ClientRequestId)', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND INDEX_NAME='UX_fn_ai_prompt_ScopeKey_Id'), 'CREATE UNIQUE INDEX UX_fn_ai_prompt_ScopeKey_Id ON fn_ai_prompt (ScopeKey,Id)', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND INDEX_NAME='IX_fn_ai_prompt_ScopeKey_CreatedAtUtc_Id'), 'CREATE INDEX IX_fn_ai_prompt_ScopeKey_CreatedAtUtc_Id ON fn_ai_prompt (ScopeKey,CreatedAtUtc DESC,Id)', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND INDEX_NAME='UX_fn_ai_prompt_version_PromptId_Version'), 'CREATE UNIQUE INDEX UX_fn_ai_prompt_version_PromptId_Version ON fn_ai_prompt_version (PromptId,Version)', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND INDEX_NAME='UX_fn_ai_prompt_version_ScopeKey_PromptId_Id'), 'CREATE UNIQUE INDEX UX_fn_ai_prompt_version_ScopeKey_PromptId_Id ON fn_ai_prompt_version (ScopeKey,PromptId,Id)', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND CONSTRAINT_NAME='FK_fn_ai_prompt_version_ScopeKey_PromptId'), 'ALTER TABLE fn_ai_prompt_version ADD CONSTRAINT FK_fn_ai_prompt_version_ScopeKey_PromptId FOREIGN KEY (ScopeKey,PromptId) REFERENCES fn_ai_prompt (ScopeKey,Id)', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND CONSTRAINT_NAME='FK_fn_ai_prompt_ScopeKey_Id_ActiveVersionId'), 'ALTER TABLE fn_ai_prompt ADD CONSTRAINT FK_fn_ai_prompt_ScopeKey_Id_ActiveVersionId FOREIGN KEY (ScopeKey,Id,ActiveVersionId) REFERENCES fn_ai_prompt_version (ScopeKey,PromptId,Id)', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND CONSTRAINT_NAME='CK_fn_ai_prompt_State'), 'ALTER TABLE fn_ai_prompt ADD CONSTRAINT CK_fn_ai_prompt_State CHECK (Revision>=1 AND LastVersion>=1 AND Revision>=LastVersion AND Epoch>=0 AND ((TenantId IS NULL AND ScopeKey=''host'' AND OCTET_LENGTH(ScopeKey)=4) OR (TenantId IS NOT NULL AND ScopeKey=LOWER(HEX(TenantId)) AND OCTET_LENGTH(ScopeKey)=32)) AND (OCTET_LENGTH(CreateHash)=64 AND CreateHash NOT REGEXP ''[^0-9a-f]''))', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND CONSTRAINT_NAME='CK_fn_ai_prompt_State' AND ENFORCED='NO'), 'ALTER TABLE fn_ai_prompt ALTER CHECK CK_fn_ai_prompt_State ENFORCED', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND CONSTRAINT_NAME='CK_fn_ai_prompt_version_State'), 'ALTER TABLE fn_ai_prompt_version ADD CONSTRAINT CK_fn_ai_prompt_version_State CHECK (Version>=1 AND FormatVersion=1 AND ModelConfigVersion>=1 AND ((TenantId IS NULL AND ScopeKey=''host'' AND OCTET_LENGTH(ScopeKey)=4) OR (TenantId IS NOT NULL AND ScopeKey=LOWER(HEX(TenantId)) AND OCTET_LENGTH(ScopeKey)=32)) AND (OCTET_LENGTH(BodyHash)=64 AND BodyHash NOT REGEXP ''[^0-9a-f]'') AND JSON_VALID(BodyJson)=1 AND WasPublished IN (0,1))', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND CONSTRAINT_NAME='CK_fn_ai_prompt_version_State' AND ENFORCED='NO'), 'ALTER TABLE fn_ai_prompt_version ALTER CHECK CK_fn_ai_prompt_version_State ENFORCED', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND TABLE_COMMENT<>'当前作用域的 Prompt 目录与发布指针；停用代次撤销旧运行'), 'ALTER TABLE fn_ai_prompt COMMENT=''当前作用域的 Prompt 目录与发布指针；停用代次撤销旧运行''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='Id' AND COLUMN_COMMENT<>'目录逻辑主键'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN Id binary(16) NOT NULL COMMENT ''目录逻辑主键''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='ScopeKey' AND COLUMN_COMMENT<>'可信宿主 host 或租户 UUID 规范串'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN ScopeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''可信宿主 host 或租户 UUID 规范串''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='TenantId' AND COLUMN_COMMENT<>'可信租户；宿主为空'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN TenantId binary(16) NULL COMMENT ''可信租户；宿主为空''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='OwnerUserId' AND COLUMN_COMMENT<>'目录创建者；不构成额外授权'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN OwnerUserId binary(16) NOT NULL COMMENT ''目录创建者；不构成额外授权''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='ClientRequestId' AND COLUMN_COMMENT<>'创建者在作用域内的幂等键'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN ClientRequestId binary(16) NOT NULL COMMENT ''创建者在作用域内的幂等键''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='CreateHash' AND COLUMN_COMMENT<>'首版请求的规范载荷摘要'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN CreateHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''首版请求的规范载荷摘要''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='Name' AND COLUMN_COMMENT<>'目录显示名称'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN Name varchar(128) NOT NULL COMMENT ''目录显示名称''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='Revision' AND COLUMN_COMMENT<>'目录并发修订号'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN Revision int NOT NULL COMMENT ''目录并发修订号''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='LastVersion' AND COLUMN_COMMENT<>'最后追加的不可变版本号'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN LastVersion int NOT NULL COMMENT ''最后追加的不可变版本号''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='ActiveVersionId' AND COLUMN_COMMENT<>'显式发布版本；停用或未发布时为空'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN ActiveVersionId binary(16) NULL COMMENT ''显式发布版本；停用或未发布时为空''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='Epoch' AND COLUMN_COMMENT<>'停用撤销代次；重新发布不会恢复旧代次'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN Epoch int NOT NULL COMMENT ''停用撤销代次；重新发布不会恢复旧代次''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='CreatedAtUtc' AND COLUMN_COMMENT<>'目录创建 UTC 时间'), 'ALTER TABLE fn_ai_prompt MODIFY COLUMN CreatedAtUtc datetime(6) NOT NULL COMMENT ''目录创建 UTC 时间''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND TABLE_COMMENT<>'不可变纯文本 Prompt 正文与模型身份；首次发布标记不改写正文'), 'ALTER TABLE fn_ai_prompt_version COMMENT=''不可变纯文本 Prompt 正文与模型身份；首次发布标记不改写正文''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='Id' AND COLUMN_COMMENT<>'不可变版本逻辑主键'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN Id binary(16) NOT NULL COMMENT ''不可变版本逻辑主键''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='PromptId' AND COLUMN_COMMENT<>'所属 AI 目录'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN PromptId binary(16) NOT NULL COMMENT ''所属 AI 目录''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='ScopeKey' AND COLUMN_COMMENT<>'与所属目录相同的可信作用域'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN ScopeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''与所属目录相同的可信作用域''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='TenantId' AND COLUMN_COMMENT<>'可信租户；宿主为空'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN TenantId binary(16) NULL COMMENT ''可信租户；宿主为空''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='Version' AND COLUMN_COMMENT<>'目录内不可变版本号'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN Version int NOT NULL COMMENT ''目录内不可变版本号''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='FormatVersion' AND COLUMN_COMMENT<>'闭合纯文本策略协议版本'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN FormatVersion int NOT NULL COMMENT ''闭合纯文本策略协议版本''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='BodyJson' AND COLUMN_COMMENT<>'源生成规范正文；固定文本 Schema、零工具及无检索'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN BodyJson longtext NOT NULL COMMENT ''源生成规范正文；固定文本 Schema、零工具及无检索''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='ModelConfigVersion' AND COLUMN_COMMENT<>'草稿创建时绑定的模型配置修订号'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN ModelConfigVersion int NOT NULL COMMENT ''草稿创建时绑定的模型配置修订号''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='ProviderKey' AND COLUMN_COMMENT<>'绑定的提供程序稳定键'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN ProviderKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''绑定的提供程序稳定键''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='ModelId' AND COLUMN_COMMENT<>'绑定的模型稳定标识'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN ModelId varchar(256) NOT NULL COMMENT ''绑定的模型稳定标识''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='BodyHash' AND COLUMN_COMMENT<>'完整规范正文和模型身份的 SHA256 摘要'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN BodyHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT ''完整规范正文和模型身份的 SHA256 摘要''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='WasPublished' AND COLUMN_COMMENT<>'是否曾经显式发布；不因停用清除'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN WasPublished tinyint NOT NULL COMMENT ''是否曾经显式发布；不因停用清除''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='CreatedByUserId' AND COLUMN_COMMENT<>'版本创建者'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN CreatedByUserId binary(16) NOT NULL COMMENT ''版本创建者''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @FnAiPromptDdl := IF(EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt_version' AND COLUMN_NAME='CreatedAtUtc' AND COLUMN_COMMENT<>'版本创建 UTC 时间'), 'ALTER TABLE fn_ai_prompt_version MODIFY COLUMN CreatedAtUtc datetime(6) NOT NULL COMMENT ''版本创建 UTC 时间''', 'SELECT 1');
PREPARE stmt FROM @FnAiPromptDdl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
