-- 032：清理 fn_auditing_exception_log 中升级前遗留的敏感异常消息与堆栈。
-- MySQL 直接按表的 utf8mb4 字符集写入统一占位消息；与 SQL Server 脚本保持相同脱敏语义。
-- 本迁移会不可逆覆盖 Message 并清空 StackTrace，执行前必须按发布流程确认备份与审计留存策略。
UPDATE fn_auditing_exception_log
SET Message = 'Unhandled application exception.',
    StackTrace = NULL
WHERE Message <> 'Unhandled application exception.'
   OR StackTrace IS NOT NULL;
