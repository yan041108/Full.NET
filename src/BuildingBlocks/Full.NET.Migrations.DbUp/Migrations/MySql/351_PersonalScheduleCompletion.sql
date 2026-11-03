-- 本人日程原操作Inbox及准确接收方回执，不保存日程正文
CREATE TABLE IF NOT EXISTS fn_calendar_schedule_completion (
        Id binary(16) NOT NULL COMMENT '应用端UUID v7',
        TenantId binary(16) NULL COMMENT '可信租户，Host为空',
        ScopeKey varchar(32) NOT NULL COMMENT '可信范围唯一键',
        OwnerUserId binary(16) NOT NULL COMMENT '原操作本人主体',
        RunId binary(16) NOT NULL COMMENT '真实AI运行身份',
        OperationId binary(16) NOT NULL COMMENT '原写节点稳定操作身份',
        ScheduleId binary(16) NOT NULL COMMENT '准确个人日程身份，无跨模块外键',
        ExpectedVersion int NOT NULL COMMENT '原批准pending版本',
        RequestDigest char(64) NOT NULL COMMENT '不可更换原请求摘要',
        Outcome varchar(16) NOT NULL COMMENT 'completed或rejected实际事实',
        Reason varchar(48) NULL COMMENT '安全拒绝原因',
        ActualVersion int NULL COMMENT '成功CAS后的准确版本，拒绝为空',
        CompletedAtUtc datetime(6) NULL COMMENT '本操作实际完成UTC时间',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '原操作回执提交UTC时间',
        CONSTRAINT PK_fn_calendar_schedule_completion PRIMARY KEY (Id),
        CONSTRAINT UX_fn_calendar_schedule_completion_Operation UNIQUE (ScopeKey,OperationId)
) COMMENT='本人日程原操作Inbox及准确接收方回执，不保存日程正文' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
-- 独立批准消费与不可变发送意图，同AI Outbox原子提交
CREATE TABLE IF NOT EXISTS fn_ai_schedule_completion_intent (
        Id binary(16) NOT NULL COMMENT '应用端UUID v7',
        TenantId binary(16) NULL COMMENT '可信租户，Host为空',
        ScopeKey varchar(32) NOT NULL COMMENT '可信范围唯一键',
        OwnerUserId binary(16) NOT NULL COMMENT '原运行主体',
        RunId binary(16) NOT NULL COMMENT '真实静态运行身份',
        OperationId binary(16) NOT NULL COMMENT '原写节点稳定操作身份',
        RequestJson mediumtext NOT NULL COMMENT '服务器冻结原会话与准确批准参数，不公开返回',
        ScheduleId binary(16) NOT NULL COMMENT '不可变准确日程目标，原操作唯一占用',
        ExpectedVersion int NOT NULL COMMENT '原操作准确pending版本，未知不得换新操作',
        RequestDigest char(64) NOT NULL COMMENT '不可变原请求摘要',
        Outcome varchar(16) NOT NULL COMMENT 'accepted或unknown或接收方completed或rejected',
        ReceiptJson mediumtext NULL COMMENT '准确Calendar原操作回执，尚未确认时为空',
        Version int NOT NULL COMMENT '意图结果乐观版本',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '意图提交UTC时间',
        UpdatedAtUtc datetime(6) NOT NULL COMMENT '结果核对UTC时间',
        CONSTRAINT PK_fn_ai_schedule_completion_intent PRIMARY KEY (Id),
        CONSTRAINT UX_fn_ai_schedule_completion_intent_Operation UNIQUE (ScopeKey,OperationId),
        CONSTRAINT UX_fn_ai_schedule_completion_intent_Target UNIQUE (ScopeKey,ScheduleId,ExpectedVersion)
) COMMENT='独立批准消费与不可变发送意图，同AI Outbox原子提交' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
-- 单个DDL提交后重入仍补齐原操作唯一键与闭合状态约束。
DROP PROCEDURE IF EXISTS fn_calendar_ensure_completion;
DELIMITER $$
CREATE PROCEDURE fn_calendar_ensure_completion()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_calendar_schedule_completion' AND INDEX_NAME='UX_fn_calendar_schedule_completion_Operation') THEN
        CREATE UNIQUE INDEX UX_fn_calendar_schedule_completion_Operation ON fn_calendar_schedule_completion (ScopeKey,OperationId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_calendar_schedule_completion' AND CONSTRAINT_NAME='CK_fn_calendar_schedule_completion_Bounds') THEN
        ALTER TABLE fn_calendar_schedule_completion ADD CONSTRAINT CK_fn_calendar_schedule_completion_Bounds CHECK (ExpectedVersion>0 AND ExpectedVersion<2147483647 AND ((Outcome='completed' AND Reason IS NULL AND ActualVersion IS NOT NULL AND ActualVersion=ExpectedVersion+1 AND CompletedAtUtc IS NOT NULL) OR (Outcome='rejected' AND Reason IS NOT NULL AND Reason IN ('authorization_unavailable','intent_expired','target_changed') AND ActualVersion IS NULL AND CompletedAtUtc IS NULL)));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_schedule_completion_intent' AND INDEX_NAME='UX_fn_ai_schedule_completion_intent_Operation') THEN
        CREATE UNIQUE INDEX UX_fn_ai_schedule_completion_intent_Operation ON fn_ai_schedule_completion_intent (ScopeKey,OperationId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_schedule_completion_intent' AND CONSTRAINT_NAME='CK_fn_ai_schedule_completion_intent_Bounds') THEN
        ALTER TABLE fn_ai_schedule_completion_intent ADD CONSTRAINT CK_fn_ai_schedule_completion_intent_Bounds CHECK (Version>0 AND ExpectedVersion>0 AND ExpectedVersion<2147483647 AND ((Outcome IN ('accepted','unknown') AND ReceiptJson IS NULL) OR (Outcome IN ('completed','rejected') AND ReceiptJson IS NOT NULL)));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_schedule_completion_intent' AND INDEX_NAME='UX_fn_ai_schedule_completion_intent_Target') THEN
        CREATE UNIQUE INDEX UX_fn_ai_schedule_completion_intent_Target ON fn_ai_schedule_completion_intent (ScopeKey,ScheduleId,ExpectedVersion);
    END IF;
END$$
DELIMITER ;
CALL fn_calendar_ensure_completion();
DROP PROCEDURE IF EXISTS fn_calendar_ensure_completion;
