-- 038：当前用户 Grid 列展示偏好。

CREATE TABLE IF NOT EXISTS fn_settings_user_grid_preference (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    GridKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '表格键',
    SchemaVersion int NOT NULL COMMENT 'Schema 版本',
    ColumnsJson json NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_settings_user_grid_preference PRIMARY KEY (Id),
    CONSTRAINT CK_fn_settings_user_grid_preference_SchemaVersion
        CHECK (SchemaVersion > 0),
    UNIQUE KEY UX_fn_settings_user_grid_preference_UserGrid (UserId, GridKey)
) COMMENT='系统设置user grid preference表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- MySQL DDL 会隐式提交，单独修复索引可覆盖表已创建但索引缺失的半完成状态。
DROP PROCEDURE IF EXISTS fn_settings_grid_preference_index;
DELIMITER $$
CREATE PROCEDURE fn_settings_grid_preference_index()
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_settings_user_grid_preference'
          AND INDEX_NAME = 'UX_fn_settings_user_grid_preference_UserGrid'
    )
    AND
    (
        (
            SELECT MAX(NON_UNIQUE)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_settings_user_grid_preference'
              AND INDEX_NAME = 'UX_fn_settings_user_grid_preference_UserGrid'
        ) <> 0
        OR
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_settings_user_grid_preference'
              AND INDEX_NAME = 'UX_fn_settings_user_grid_preference_UserGrid'
        ) <> 2
        OR EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_settings_user_grid_preference'
              AND INDEX_NAME = 'UX_fn_settings_user_grid_preference_UserGrid'
              AND SUB_PART IS NOT NULL
        )
        OR NOT EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_settings_user_grid_preference'
              AND INDEX_NAME = 'UX_fn_settings_user_grid_preference_UserGrid'
              AND SEQ_IN_INDEX = 1
              AND COLUMN_NAME = 'UserId'
        )
        OR NOT EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_settings_user_grid_preference'
              AND INDEX_NAME = 'UX_fn_settings_user_grid_preference_UserGrid'
              AND SEQ_IN_INDEX = 2
              AND COLUMN_NAME = 'GridKey'
        )
    ) THEN
        DROP INDEX UX_fn_settings_user_grid_preference_UserGrid
            ON fn_settings_user_grid_preference;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_settings_user_grid_preference'
          AND INDEX_NAME = 'UX_fn_settings_user_grid_preference_UserGrid'
    ) THEN
        CREATE UNIQUE INDEX UX_fn_settings_user_grid_preference_UserGrid
            ON fn_settings_user_grid_preference(UserId, GridKey);
    END IF;
END$$
DELIMITER ;

CALL fn_settings_grid_preference_index();
DROP PROCEDURE fn_settings_grid_preference_index;
