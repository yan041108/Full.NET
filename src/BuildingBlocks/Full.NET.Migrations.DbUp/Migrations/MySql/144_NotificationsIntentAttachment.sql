-- 144：通知 Intent 邮件附件投影表，供 Files claim 探测与 Worker 有界装载。

CREATE TABLE IF NOT EXISTS fn_notifications_intent_attachment (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    IntentId BINARY(16) NOT NULL COMMENT '通知意图标识',
    FileId BINARY(16) NOT NULL COMMENT '附件文件标识',
    SortOrder int NOT NULL COMMENT '排序序号',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_notifications_intent_attachment PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_intent_attachment_Intent
        FOREIGN KEY (IntentId) REFERENCES fn_notifications_intent(Id),
    CONSTRAINT UX_fn_notifications_intent_attachment_IntentFile
        UNIQUE (IntentId, FileId),
    CONSTRAINT CK_fn_notifications_intent_attachment_SortOrder CHECK (SortOrder >= 0)
) COMMENT='通知意图邮件附件投影表' ENGINE=InnoDB;

CREATE INDEX IX_fn_notifications_intent_attachment_IntentId
    ON fn_notifications_intent_attachment (IntentId);

CREATE INDEX IX_fn_notifications_intent_attachment_FileId
    ON fn_notifications_intent_attachment (FileId);
