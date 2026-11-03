-- 338：能力与原Files持有共用权威边界，不借用Workflow事务。
CREATE TABLE IF NOT EXISTS fn_files_read_capability (
 Id binary(16) NOT NULL COMMENT '不透明读取能力UUID v7',
 CallId binary(16) NOT NULL COMMENT '固定Workflow调用身份，无跨模块外键',
 IssuerTenantId binary(16) NULL COMMENT '签发租户，空表示Host',
 IssuerScopeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信签发作用域',
 RecipientTenantId binary(16) NULL COMMENT '接收租户，空表示Host',
 RecipientScopeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定可信接收作用域',
 DirectionKey varchar(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'input或result协议方向',
 FieldKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '最初接收方字段键',
 SourceFileId binary(16) NOT NULL COMMENT 'Files内部原文件身份，不返回接收方',
 OwnerReferenceId binary(16) NOT NULL COMMENT '本地活动引用或租户资源所有权身份',
 OriginalFileName varchar(255) NOT NULL COMMENT '安全下载名快照',
 ContentType varchar(128) NOT NULL COMMENT '内容类型快照',
 SizeBytes bigint NOT NULL COMMENT '实际文件字节数快照',
 ContentHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定小写SHA256内容摘要',
 StatusKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'active或revoked状态',
 ExpiresAtUtc datetime(6) NOT NULL COMMENT '不可延长的读取截止UTC',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '能力首次签发UTC',
 RevokedAtUtc datetime(6) NULL COMMENT '撤销或到期收敛UTC',
 Revision bigint NOT NULL COMMENT '单调修订号',
 CONSTRAINT pk_fn_files_read_capability PRIMARY KEY (Id)
) COMMENT='Workflow关联的不透明文件读取能力，接收方不获得原文件身份' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
DROP PROCEDURE IF EXISTS fn_files_read_capability_ensure;
DELIMITER $$
CREATE PROCEDURE fn_files_read_capability_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_files_read_capability' AND INDEX_NAME='ux_fn_files_read_capability_key') THEN
  CREATE UNIQUE INDEX ux_fn_files_read_capability_key ON fn_files_read_capability(CallId,IssuerScopeKey,DirectionKey,FieldKey,SourceFileId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_files_read_capability' AND INDEX_NAME='ix_fn_files_read_capability_expiry') THEN
  CREATE  INDEX ix_fn_files_read_capability_expiry ON fn_files_read_capability(StatusKey,ExpiresAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_files_read_capability' AND INDEX_NAME='ix_fn_files_read_capability_issuer') THEN
  CREATE  INDEX ix_fn_files_read_capability_issuer ON fn_files_read_capability(CallId,IssuerScopeKey,StatusKey,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_files_read_capability' AND CONSTRAINT_NAME='ck_fn_files_read_capability_state') THEN
  ALTER TABLE fn_files_read_capability ADD CONSTRAINT ck_fn_files_read_capability_state CHECK (Revision>0 AND SizeBytes>0 AND IssuerScopeKey<>RecipientScopeKey AND DirectionKey IN ('input','result') AND StatusKey IN ('active','revoked') AND ExpiresAtUtc>CreatedAtUtc AND ((StatusKey='active' AND RevokedAtUtc IS NULL) OR (StatusKey='revoked' AND RevokedAtUtc IS NOT NULL)) AND ((IssuerTenantId IS NULL AND IssuerScopeKey='host') OR (IssuerTenantId IS NOT NULL AND IssuerScopeKey<>'host')) AND ((RecipientTenantId IS NULL AND RecipientScopeKey='host') OR (RecipientTenantId IS NOT NULL AND RecipientScopeKey<>'host')));
 END IF;
END$$
DELIMITER ;
CALL fn_files_read_capability_ensure();
DROP PROCEDURE IF EXISTS fn_files_read_capability_ensure;
