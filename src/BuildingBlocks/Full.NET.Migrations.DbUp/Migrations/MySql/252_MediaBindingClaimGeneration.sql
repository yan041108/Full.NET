-- 为 Identity 头像/签名及 Tenancy 标志展开绑定代际键；空值保留历史引用，不回填猜测的键。
-- MySQL DDL 非事务且不支持 ADD COLUMN IF NOT EXISTS，逐列探测恢复部分执行；回退须先停写并保留代际数据。
SET @fn_media_avatar_exists = (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_user_profile' AND COLUMN_NAME = 'AvatarClaimKey');
SET @fn_media_avatar_sql = IF(@fn_media_avatar_exists = 0, 'ALTER TABLE fn_identity_user_profile ADD AvatarClaimKey varchar(200) NULL COMMENT ''头像绑定代际引用键，空值兼容历史绑定''', 'SELECT 1');
PREPARE fn_media_avatar_stmt FROM @fn_media_avatar_sql;
EXECUTE fn_media_avatar_stmt;
DEALLOCATE PREPARE fn_media_avatar_stmt;

SET @fn_media_signature_exists = (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_user_profile' AND COLUMN_NAME = 'SignatureClaimKey');
SET @fn_media_signature_sql = IF(@fn_media_signature_exists = 0, 'ALTER TABLE fn_identity_user_profile ADD SignatureClaimKey varchar(200) NULL COMMENT ''签名绑定代际引用键，空值兼容历史绑定''', 'SELECT 1');
PREPARE fn_media_signature_stmt FROM @fn_media_signature_sql;
EXECUTE fn_media_signature_stmt;
DEALLOCATE PREPARE fn_media_signature_stmt;

SET @fn_media_logo_exists = (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_tenancy_tenant' AND COLUMN_NAME = 'LogoClaimKey');
SET @fn_media_logo_sql = IF(@fn_media_logo_exists = 0, 'ALTER TABLE fn_tenancy_tenant ADD LogoClaimKey varchar(200) NULL COMMENT ''标志绑定代际引用键，空值兼容历史绑定''', 'SELECT 1');
PREPARE fn_media_logo_stmt FROM @fn_media_logo_sql;
EXECUTE fn_media_logo_stmt;
DEALLOCATE PREPARE fn_media_logo_stmt;
