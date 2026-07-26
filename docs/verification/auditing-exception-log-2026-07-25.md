# Auditing Host 异常日志验证记录（2026-07-25）

- 范围：`fn_auditing_exception_log`；中间件捕获未处理异常后尽力写入并重抛；Host 分页/详情；Vue/Layui 只读页；Testing 探针
- 计划：[`2026-07-25-auditing-exception-log-vertical-slice.md`](../superpowers/plans/2026-07-25-auditing-exception-log-vertical-slice.md)
- 状态：**Build-verified**（保留清理与告警通道未交付；不能标记 `Verified`）

## 证据

| 层 | 结果 |
| --- | --- |
| 迁移 | `024_AuditingExceptionLog.sql` 建表；`032_AuditingExceptionLogSanitization.sql` 清理存量敏感 Message/StackTrace；SQL Server + MySQL |
| Integration | `Host_exception_log_query` SQL Server/MySQL **2/2** |
| OpenAPI 静态 | `auditing-exception-logs-contract.test.mjs` **2/2** |
| Mock parity | 「异常日志列表」× 双端 **2/2** → `shell-parity` **46 → 48** |
| 真实栈 | 新增 `host-exception-logs.spec.mjs`；门槛 **66 → 70**；完整容器矩阵由 CI 覆盖 |
| 四处 canonical | **359/7/40/172** |

## 边界

- 中间件捕获后重抛；不记 Body/QueryString，也不持久化原始异常消息和堆栈。查询 API 只返回安全占位消息、异常类型、路径、TraceId 与脱敏客户端标识。
- Testing 探针 `POST /api/v1/auditing/exception-probes` 不得进入非 Testing 环境。
- 业务 `Result` 失败、告警通道、保留清理不在本切片。
