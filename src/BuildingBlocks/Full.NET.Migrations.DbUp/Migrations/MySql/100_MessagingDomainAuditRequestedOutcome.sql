-- 100：Kafka 范围重放在执行前写 requested 审计，约束必须覆盖该稳定状态。
DROP PROCEDURE IF EXISTS fn_messaging_domain_audit_requested_outcome;
DELIMITER $$
CREATE PROCEDURE fn_messaging_domain_audit_requested_outcome()
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_domain_audit'
          AND CONSTRAINT_NAME = 'CK_fn_messaging_domain_audit_Outcome'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_messaging_domain_audit
            DROP CHECK CK_fn_messaging_domain_audit_Outcome;
    END IF;

    ALTER TABLE fn_messaging_domain_audit
        ADD CONSTRAINT CK_fn_messaging_domain_audit_Outcome
            CHECK (Outcome IN ('requested', 'success', 'failure'));
END$$
DELIMITER ;

CALL fn_messaging_domain_audit_requested_outcome();
DROP PROCEDURE IF EXISTS fn_messaging_domain_audit_requested_outcome;
