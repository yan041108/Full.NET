-- 090：MySQL DDL 会隐式提交，逐项收敛系统配置项的新增分组列。
-- 与 SqlServer 090 保持同构：配置分组 GroupName，对应 Admin.NET 配置分组下拉。
DROP PROCEDURE IF EXISTS fn_settings_config_entry_group_name;
DELIMITER $$
CREATE PROCEDURE fn_settings_config_entry_group_name()
BEGIN
    -- 系统配置项：配置分组，允许为空，存量行保持 NULL，由代码层规范化空值。
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_settings_config_entry'
          AND COLUMN_NAME = 'GroupName'
    ) THENALTER TABLE fn_settings_config_entry ADD GroupName varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '分组名称'
    END IF;
END$$
DELIMITER ;

CALL fn_settings_config_entry_group_name();
DROP PROCEDURE fn_settings_config_entry_group_name;
