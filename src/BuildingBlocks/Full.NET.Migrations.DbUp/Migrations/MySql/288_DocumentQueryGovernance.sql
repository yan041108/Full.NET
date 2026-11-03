-- 分类树事务闸门与逻辑访问去重；MySQL 隐式提交的每一步独立探测，不删除历史事实。
CREATE TABLE IF NOT EXISTS fn_document_category_guard (
    Id binary(16) NOT NULL COMMENT '预生成的 UUID v7 Host 闸门标识',
    Version bigint NOT NULL COMMENT '取得锁时递增的闸门版本',
    CONSTRAINT PK_fn_document_category_guard PRIMARY KEY (Id)
) COMMENT='文档分类树事务闸门表';
INSERT INTO fn_document_category_guard (Id, Version)
SELECT UNHEX('019a1900000070008000000000000001'), 1
WHERE NOT EXISTS (SELECT 1 FROM fn_document_category_guard WHERE Id = UNHEX('019a1900000070008000000000000001'));
DROP PROCEDURE IF EXISTS fn_migrate_document_query_governance;
DELIMITER $$
CREATE PROCEDURE fn_migrate_document_query_governance()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND COLUMN_NAME = 'OperationKey') THEN
        ALTER TABLE fn_document_access_log ADD OperationKey varchar(192) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '逻辑访问身份，重试与 Range 请求归并';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_document_access_log' AND INDEX_NAME = 'UX_fn_document_access_log_OperationKey') THEN
        CREATE UNIQUE INDEX UX_fn_document_access_log_OperationKey ON fn_document_access_log (OperationKey);
    END IF;
END$$
DELIMITER ;
CALL fn_migrate_document_query_governance();
DROP PROCEDURE IF EXISTS fn_migrate_document_query_governance;
