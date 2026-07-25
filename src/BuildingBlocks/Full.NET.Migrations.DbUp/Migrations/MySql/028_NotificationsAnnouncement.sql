-- 028：Host 作用域公告主数据。

CREATE TABLE IF NOT EXISTS fn_notifications_announcement
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    Title varchar(200) NOT NULL,
    Content varchar(4000) NOT NULL,
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PublishedAtUtc datetime(6) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    CreatedByUserId BINARY(16) NOT NULL,
    UpdatedByUserId BINARY(16) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_notifications_announcement PRIMARY KEY (Id),
    KEY IX_fn_notifications_announcement_CreatedAtUtc (CreatedAtUtc, Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
