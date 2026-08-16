-- 028：Host 作用域公告主数据。

CREATE TABLE IF NOT EXISTS fn_notifications_announcement (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    Title varchar(200) NOT NULL COMMENT '标题',
    Content varchar(4000) NOT NULL COMMENT '内容',
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态',
    PublishedAtUtc datetime(6) NULL COMMENT '发布时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',
    UpdatedByUserId BINARY(16) NULL COMMENT '更新人用户标识',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_notifications_announcement PRIMARY KEY (Id),
    KEY IX_fn_notifications_announcement_CreatedAtUtc (CreatedAtUtc, Id)
) COMMENT='通知公告表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
