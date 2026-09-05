-- 117：通知模板 BCP 47 语言变体；同一 TemplateKey 可按 LocaleTag 维护多份草稿与发布版本。
SET @locale_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_notifications_template'
      AND COLUMN_NAME = 'LocaleTag');
SET @ddl := IF(
    @locale_exists = 0,
    'ALTER TABLE fn_notifications_template ADD COLUMN LocaleTag varchar(35) COLLATE utf8mb4_bin NOT NULL DEFAULT ''zh-CN''',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @default_locale_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_notifications_template'
      AND COLUMN_NAME = 'DefaultLocaleTag');
SET @ddl := IF(
    @default_locale_exists = 0,
    'ALTER TABLE fn_notifications_template ADD COLUMN DefaultLocaleTag varchar(35) COLLATE utf8mb4_bin NOT NULL DEFAULT ''zh-CN''',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @version_locale_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_notifications_template_version'
      AND COLUMN_NAME = 'LocaleTag');
SET @ddl := IF(
    @version_locale_exists = 0,
    'ALTER TABLE fn_notifications_template_version ADD COLUMN LocaleTag varchar(35) COLLATE utf8mb4_bin NOT NULL DEFAULT ''zh-CN''',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE fn_notifications_template
SET LocaleTag = 'zh-CN',
    DefaultLocaleTag = 'zh-CN'
WHERE LocaleTag IS NULL OR DefaultLocaleTag IS NULL;

UPDATE fn_notifications_template_version v
INNER JOIN fn_notifications_template t ON t.Id = v.TemplateId
SET v.LocaleTag = t.LocaleTag
WHERE v.LocaleTag IS NULL OR v.LocaleTag = '';

SET @old_index_exists := (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_notifications_template'
      AND INDEX_NAME = 'UX_fn_notifications_template_Scope_Key');
SET @ddl := IF(
    @old_index_exists > 0,
    'ALTER TABLE fn_notifications_template DROP INDEX UX_fn_notifications_template_Scope_Key',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @new_index_exists := (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_notifications_template'
      AND INDEX_NAME = 'UX_fn_notifications_template_Scope_Key_Locale');
SET @ddl := IF(
    @new_index_exists = 0,
    'CREATE UNIQUE INDEX UX_fn_notifications_template_Scope_Key_Locale ON fn_notifications_template (TenantScopeKey, TemplateKey, LocaleTag)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
