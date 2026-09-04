-- 106：扩展 Host 公告类型、受众与发布/撤回生命周期，并引入规范化受众子表。
SET @db := DATABASE();

SET @kind_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = @db
      AND TABLE_NAME = 'fn_notifications_announcement'
      AND COLUMN_NAME = 'Kind');
SET @sql := IF(
    @kind_exists = 0,
    'ALTER TABLE fn_notifications_announcement ADD COLUMN Kind varchar(32) NOT NULL DEFAULT ''announcement'' AFTER Content',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @audience_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = @db
      AND TABLE_NAME = 'fn_notifications_announcement'
      AND COLUMN_NAME = 'AudienceKind');
SET @sql := IF(
    @audience_exists = 0,
    'ALTER TABLE fn_notifications_announcement ADD COLUMN AudienceKind varchar(32) NOT NULL DEFAULT ''all'' AFTER Kind',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @published_by_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = @db
      AND TABLE_NAME = 'fn_notifications_announcement'
      AND COLUMN_NAME = 'PublishedByUserId');
SET @sql := IF(
    @published_by_exists = 0,
    'ALTER TABLE fn_notifications_announcement ADD COLUMN PublishedByUserId BINARY(16) NULL AFTER PublishedAtUtc',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @retracted_at_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = @db
      AND TABLE_NAME = 'fn_notifications_announcement'
      AND COLUMN_NAME = 'RetractedAtUtc');
SET @sql := IF(
    @retracted_at_exists = 0,
    'ALTER TABLE fn_notifications_announcement ADD COLUMN RetractedAtUtc datetime(6) NULL AFTER PublishedByUserId',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @retracted_by_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = @db
      AND TABLE_NAME = 'fn_notifications_announcement'
      AND COLUMN_NAME = 'RetractedByUserId');
SET @sql := IF(
    @retracted_by_exists = 0,
    'ALTER TABLE fn_notifications_announcement ADD COLUMN RetractedByUserId BINARY(16) NULL AFTER RetractedAtUtc',
    'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

CREATE TABLE IF NOT EXISTS fn_notifications_announcement_target_user (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    AnnouncementId BINARY(16) NOT NULL COMMENT '公告标识',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    CONSTRAINT PK_fn_notifications_announcement_target_user PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_announcement_target_user_Announcement
        FOREIGN KEY (AnnouncementId) REFERENCES fn_notifications_announcement(Id),
    UNIQUE KEY UX_fn_notifications_announcement_target_user (AnnouncementId, UserId)
) COMMENT='公告用户受众表';

CREATE TABLE IF NOT EXISTS fn_notifications_announcement_target_organization (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    AnnouncementId BINARY(16) NOT NULL COMMENT '公告标识',
    TenantId BINARY(16) NOT NULL COMMENT '机构所属租户标识',
    OrganizationUnitId BINARY(16) NOT NULL COMMENT '机构单元标识',
    CONSTRAINT PK_fn_notifications_announcement_target_organization PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notif_ann_target_org_Announcement
        FOREIGN KEY (AnnouncementId) REFERENCES fn_notifications_announcement(Id),
    UNIQUE KEY UX_fn_notifications_announcement_target_organization (AnnouncementId, TenantId, OrganizationUnitId)
) COMMENT='公告机构受众表';
