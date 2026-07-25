# Auditing Host 操作日志验证记录（2026-07-25）

- 范围：`fn_auditing_operation_log`；已认证 POST/PUT/PATCH/DELETE 中间件尽力写入；Host 分页/详情；Vue/Layui 只读页
- 计划：[`2026-07-25-auditing-operation-log-vertical-slice.md`](../superpowers/plans/2026-07-25-auditing-operation-log-vertical-slice.md)
- 状态：**Build-verified**（异常日志仍 Mapped；不能标记 `Verified`）

## 证据

| 层 | 结果 |
| --- | --- |
| 迁移 | `023_AuditingOperationLog.sql` SQL Server + MySQL |
| Integration | `Host_operation_log_query` SQL Server/MySQL **2/2** |
| OpenAPI 静态 | `auditing-operation-logs-contract.test.mjs` **2/2** |
| Mock parity | 「操作日志列表」× 双端 **2/2** → `shell-parity` **44 → 46** |
| 真实栈 | 新增 `host-operation-logs.spec.mjs`；门槛 **62 → 66**；完整容器矩阵由 CI 覆盖 |
| 四处 canonical | **349/7/38/144** |

## 边界

- 仅已认证写方法；不记 Body/QueryString。
- 异常日志、业务 Handler 显式埋点、保留清理不在本切片。
