-- 236：系统自动恢复没有用户操作者，允许工作流领域审计保留空操作者。
ALTER TABLE fn_workflow_domain_audit
    MODIFY COLUMN ActorUserId BINARY(16) NULL COMMENT '操作者用户标识；NULL 表示系统操作';
