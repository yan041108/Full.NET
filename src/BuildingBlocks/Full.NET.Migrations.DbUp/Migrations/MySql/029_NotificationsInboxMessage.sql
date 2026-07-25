-- 029：用户站内信收件箱。

CREATE TABLE IF NOT EXISTS fn_notifications_inbox_message
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    RecipientUserId BINARY(16) NOT NULL,
    Title varchar(200) NOT NULL,
    Content varchar(4000) NOT NULL,
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ReadAtUtc datetime(6) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    CreatedByUserId BINARY(16) NULL,
    CONSTRAINT PK_fn_notifications_inbox_message PRIMARY KEY (Id),
    KEY IX_fn_notifications_inbox_message_RecipientCreatedAtUtc (RecipientUserId, CreatedAtUtc, Id),
    KEY IX_fn_notifications_inbox_message_RecipientUnread (RecipientUserId, Status, Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
