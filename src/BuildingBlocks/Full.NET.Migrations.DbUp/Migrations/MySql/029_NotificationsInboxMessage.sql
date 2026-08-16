-- 029：用户站内信收件箱。

CREATE TABLE IF NOT EXISTS fn_notifications_inbox_message (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    RecipientUserId BINARY(16) NOT NULL COMMENT '接收人用户标识',
    Title varchar(200) NOT NULL COMMENT '标题',
    Content varchar(4000) NOT NULL COMMENT '内容',
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态',
    ReadAtUtc datetime(6) NULL COMMENT '已读时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedByUserId BINARY(16) NULL COMMENT '创建人用户标识',
    CONSTRAINT PK_fn_notifications_inbox_message PRIMARY KEY (Id),
    KEY IX_fn_notifications_inbox_message_RecipientCreatedAtUtc (RecipientUserId, CreatedAtUtc, Id),
    KEY IX_fn_notifications_inbox_message_RecipientUnread (RecipientUserId, Status, Id)
) COMMENT='通知收件箱消息表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
