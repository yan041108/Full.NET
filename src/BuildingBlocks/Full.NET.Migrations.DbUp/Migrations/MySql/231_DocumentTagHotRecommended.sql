-- 231：文档标签热门/推荐运营标记。

SET @col_exists := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_document_tag'
      AND COLUMN_NAME = 'IsHot');
SET @ddl := IF(
    @col_exists = 0,
    'ALTER TABLE fn_document_tag ADD COLUMN IsHot TINYINT(1) NOT NULL DEFAULT 0',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @col_exists := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_document_tag'
      AND COLUMN_NAME = 'IsRecommended');
SET @ddl := IF(
    @col_exists = 0,
    'ALTER TABLE fn_document_tag ADD COLUMN IsRecommended TINYINT(1) NOT NULL DEFAULT 0',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
