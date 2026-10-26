-- 发起人选择在启动事务中固定；后续激活只消费不可变输入并复核当前资格。
CREATE TABLE IF NOT EXISTS fn_workflow_starter_selection (
    Id BINARY(16) NOT NULL COMMENT '实例标识，同时作为一对一选择快照主键',
    SelectionsJson LONGTEXT CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '有界选择键与保序成员数组；不包含租户或发起人声明',
    SelectionsHash VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '规范化选择快照的SHA256摘要',
    CreatedAtUtc DATETIME(6) NOT NULL COMMENT '随实例首次启动原子保存的UTC时间',
    CONSTRAINT pk_fn_workflow_starter_selection PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_starter_selection_instance FOREIGN KEY (Id) REFERENCES fn_workflow_instance(Id)
) COMMENT='实例启动时固定的发起人自选快照；不授予永久办理资格' ENGINE=InnoDB;
DROP PROCEDURE IF EXISTS fn_workflow_restore_starter_selection_304;
DELIMITER $$
CREATE PROCEDURE fn_workflow_restore_starter_selection_304()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_starter_selection' AND CONSTRAINT_NAME='ck_fn_workflow_starter_selection_hash' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_workflow_starter_selection ADD CONSTRAINT ck_fn_workflow_starter_selection_hash CHECK (CHAR_LENGTH(SelectionsHash)=64);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_starter_selection' AND CONSTRAINT_NAME='ck_fn_workflow_starter_selection_json' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_workflow_starter_selection ADD CONSTRAINT ck_fn_workflow_starter_selection_json
            CHECK (JSON_VALID(SelectionsJson) AND JSON_TYPE(SelectionsJson)='OBJECT' AND OCTET_LENGTH(SelectionsJson)<=65536);
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_restore_starter_selection_304();
DROP PROCEDURE IF EXISTS fn_workflow_restore_starter_selection_304;
