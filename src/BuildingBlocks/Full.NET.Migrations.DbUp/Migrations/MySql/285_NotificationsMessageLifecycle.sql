-- 将个人归档、业务撤回与外发边界分开，支持半完成迁移重跑。
SET @notifications_lifecycle_ddl = IF(EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_delivery' AND COLUMN_NAME = 'DispatchLeaseGeneration'), 'SELECT 1', 'ALTER TABLE fn_notifications_delivery ADD COLUMN DispatchLeaseGeneration bigint NULL COMMENT ''最近一次越过外发边界的租约世代，不证明厂商已调用成功''');
PREPARE notifications_lifecycle_stmt FROM @notifications_lifecycle_ddl;
EXECUTE notifications_lifecycle_stmt;
DEALLOCATE PREPARE notifications_lifecycle_stmt;
SET @notifications_lifecycle_ddl = IF(EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_inbox_message' AND COLUMN_NAME = 'ArchivedAtUtc'), 'SELECT 1', 'ALTER TABLE fn_notifications_inbox_message ADD COLUMN ArchivedAtUtc datetime(6) NULL COMMENT ''个人归档时间（UTC），不改变已读状态''');
PREPARE notifications_lifecycle_stmt FROM @notifications_lifecycle_ddl;
EXECUTE notifications_lifecycle_stmt;
DEALLOCATE PREPARE notifications_lifecycle_stmt;
SET @notifications_lifecycle_ddl = IF(EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_inbox_message' AND COLUMN_NAME = 'RevokedAtUtc'), 'SELECT 1', 'ALTER TABLE fn_notifications_inbox_message ADD COLUMN RevokedAtUtc datetime(6) NULL COMMENT ''业务撤回时间（UTC），不承诺收回外发消息''');
PREPARE notifications_lifecycle_stmt FROM @notifications_lifecycle_ddl;
EXECUTE notifications_lifecycle_stmt;
DEALLOCATE PREPARE notifications_lifecycle_stmt;
SET @notifications_lifecycle_ddl = IF(EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_inbox_message' AND COLUMN_NAME = 'RevocationReason'), 'SELECT 1', 'ALTER TABLE fn_notifications_inbox_message ADD COLUMN RevocationReason varchar(128) NULL COMMENT ''业务撤回的审计原因''');
PREPARE notifications_lifecycle_stmt FROM @notifications_lifecycle_ddl;
EXECUTE notifications_lifecycle_stmt;
DEALLOCATE PREPARE notifications_lifecycle_stmt;
SET @notifications_lifecycle_ddl = IF(EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_inbox_message' AND COLUMN_NAME = 'DeepLinkJson'), 'SELECT 1', 'ALTER TABLE fn_notifications_inbox_message ADD COLUMN DeepLinkJson varchar(512) NULL COMMENT ''登录后受控资源引用，不构成访问授权''');
PREPARE notifications_lifecycle_stmt FROM @notifications_lifecycle_ddl;
EXECUTE notifications_lifecycle_stmt;
DEALLOCATE PREPARE notifications_lifecycle_stmt;
SET @notifications_lifecycle_ddl = IF(EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_inbox_message' AND COLUMN_NAME = 'ProducerKey'), 'SELECT 1', 'ALTER TABLE fn_notifications_inbox_message ADD COLUMN ProducerKey varchar(128) NULL COMMENT ''生成通知的业务生产者稳定键''');
PREPARE notifications_lifecycle_stmt FROM @notifications_lifecycle_ddl;
EXECUTE notifications_lifecycle_stmt;
DEALLOCATE PREPARE notifications_lifecycle_stmt;
SET @notifications_lifecycle_ddl = IF(EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_inbox_message' AND COLUMN_NAME = 'SceneKey'), 'SELECT 1', 'ALTER TABLE fn_notifications_inbox_message ADD COLUMN SceneKey varchar(128) NULL COMMENT ''业务场景稳定键，用于收件箱筛选''');
PREPARE notifications_lifecycle_stmt FROM @notifications_lifecycle_ddl;
EXECUTE notifications_lifecycle_stmt;
DEALLOCATE PREPARE notifications_lifecycle_stmt;

-- 原候选数组直接演进为元数据对象，重复恢复不嵌套包装。
UPDATE fn_notifications_intent
SET RouteSnapshotJson = JSON_OBJECT('targets', CAST(RouteSnapshotJson AS JSON))
WHERE JSON_TYPE(RouteSnapshotJson) = 'ARRAY';

-- 与 SQL Server 配对，重复恢复不会重复创建补偿索引。
SET @notifications_lifecycle_ddl = IF(EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_receipt' AND INDEX_NAME = 'IX_fn_notifications_receipt_ProcessStatusKey'), 'SELECT 1', 'CREATE INDEX IX_fn_notifications_receipt_ProcessStatusKey ON fn_notifications_receipt(ProcessStatusKey, ReceivedAtUtc, Id)');
PREPARE notifications_lifecycle_stmt FROM @notifications_lifecycle_ddl;
EXECUTE notifications_lifecycle_stmt;
DEALLOCATE PREPARE notifications_lifecycle_stmt;
SET @notifications_lifecycle_ddl = IF(EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_delivery_attempt' AND INDEX_NAME = 'IX_fn_notifications_delivery_attempt_ProviderMessageId'), 'SELECT 1', 'CREATE INDEX IX_fn_notifications_delivery_attempt_ProviderMessageId ON fn_notifications_delivery_attempt(ProviderMessageId, DeliveryId)');
PREPARE notifications_lifecycle_stmt FROM @notifications_lifecycle_ddl;
EXECUTE notifications_lifecycle_stmt;
DEALLOCATE PREPARE notifications_lifecycle_stmt;
