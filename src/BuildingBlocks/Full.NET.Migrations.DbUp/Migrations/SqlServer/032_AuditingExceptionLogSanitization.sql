-- 032：清理 fn_auditing_exception_log 中升级前遗留的敏感异常消息与堆栈。
-- SQL Server 使用 Unicode 常量写入统一占位消息；与 MySQL 脚本保持相同脱敏语义。
-- 本迁移会不可逆覆盖 Message 并清空 StackTrace，执行前必须按发布流程确认备份与审计留存策略。
UPDATE dbo.fn_auditing_exception_log
SET Message = N'Unhandled application exception.',
    StackTrace = NULL
WHERE Message <> N'Unhandled application exception.'
   OR StackTrace IS NOT NULL;
