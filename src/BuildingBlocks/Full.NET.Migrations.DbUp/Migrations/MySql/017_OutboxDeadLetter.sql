-- 为 Outbox 引入死信终态，区分可重试失败与需人工介入的毒消息。
-- MySQL DDL 会隐式提交；列追加使用 INFORMATION_SCHEMA 条件执行，保证未记账重跑能补齐剩余列。

SET @outbox_dead_letter_ddl = IF(
    EXISTS(
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_outbox_message'
          AND COLUMN_NAME = 'DeadLetteredAtUtc'),
    'SELECT 1',
    'ALTER TABLE fn_outbox_message ADD COLUMN DeadLetteredAtUtc datetime(6) NULL');
PREPARE outbox_dead_letter_statement FROM @outbox_dead_letter_ddl;
EXECUTE outbox_dead_letter_statement;
DEALLOCATE PREPARE outbox_dead_letter_statement;

SET @outbox_dead_letter_ddl = IF(
    EXISTS(
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_outbox_message'
          AND COLUMN_NAME = 'DeadLetterReasonCode'),
    'SELECT 1',
    'ALTER TABLE fn_outbox_message ADD COLUMN DeadLetterReasonCode varchar(128) NULL');
PREPARE outbox_dead_letter_statement FROM @outbox_dead_letter_ddl;
EXECUTE outbox_dead_letter_statement;
DEALLOCATE PREPARE outbox_dead_letter_statement;
