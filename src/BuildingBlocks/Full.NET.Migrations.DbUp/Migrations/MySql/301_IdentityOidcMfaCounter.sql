-- 已接受的 TOTP 步长只递增；静态条件 DDL 重跑不得重置已有消费记录。
DROP PROCEDURE IF EXISTS fn_identity_ensure_mfa_counter;
DELIMITER $$
CREATE PROCEDURE fn_identity_ensure_mfa_counter()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_user_totp' AND COLUMN_NAME = 'LastAcceptedCounter') THEN
        ALTER TABLE fn_identity_user_totp ADD COLUMN LastAcceptedCounter bigint NULL COMMENT '最近接受的中心登录 TOTP 步长，阻止重放';
    END IF;
END$$
DELIMITER ;
CALL fn_identity_ensure_mfa_counter();
DROP PROCEDURE IF EXISTS fn_identity_ensure_mfa_counter;
