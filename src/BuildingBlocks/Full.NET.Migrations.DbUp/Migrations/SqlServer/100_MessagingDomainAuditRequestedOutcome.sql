-- 100：Kafka 范围重放在执行前写 requested 审计，约束必须覆盖该稳定状态。
IF OBJECT_ID(N'dbo.fn_messaging_domain_audit', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.CK_fn_messaging_domain_audit_Outcome', N'C') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.fn_messaging_domain_audit
            DROP CONSTRAINT CK_fn_messaging_domain_audit_Outcome;
    END;

    ALTER TABLE dbo.fn_messaging_domain_audit
        ADD CONSTRAINT CK_fn_messaging_domain_audit_Outcome
            CHECK (Outcome IN ('requested', 'success', 'failure'));
END;
