# Auditing Host 访问日志验证记录（2026-07-25）

- 范围：新建 `Full.NET.Modules.Auditing`；`fn_auditing_access_log`；中间件尽力写入；Host 分页/详情查询；Vue/Layui 只读页
- 计划：[`2026-07-25-auditing-access-log-vertical-slice.md`](../superpowers/plans/2026-07-25-auditing-access-log-vertical-slice.md)
- 状态：**Build-verified**（不能标记 `Verified`：操作/异常日志、保留清理、全量真实栈矩阵仍开放）

## 证据

| 层 | 结果 |
| --- | --- |
| 迁移 | `022_AuditingAccessLog.sql` SQL Server + MySQL；高写入 NONCLUSTERED PK + `(OccurredAtUtc, Id)` 聚集/时间索引 |
| Architecture | **38/38**（含 `Auditing_declares_identity...`） |
| Integration | `Host_access_log_query` SQL Server/MySQL **2/2**（403 RED、探测请求可查、详情、OpenAPI） |
| OpenAPI 静态 | `auditing-access-logs-contract.test.mjs` **2/2** |
| `pnpm test:naming` | **23/23** |
| Vue/Layui 单测 | access-logs API/控制器各 **1/1** |
| Mock parity | 新增「访问日志列表」× 双端 → `shell-parity` **42 → 44**（本机 **2/2**；全量预计 **82 → 84**） |
| 真实栈 | 新增 `host-access-logs.spec.mjs`（管理员列表 + 受限 403）；门槛 **58 → 62**；本机未重跑完整容器矩阵，由 CI `real-stack-e2e` / `real-stack-e2e-mysql` 覆盖 |
| 四处 canonical | **349/7/38/142** |

## 边界

- `fn_identity_auth_audit` 仍属 Identity，本切片不暴露。
- 中间件不记 QueryString/Body；写库失败只记警告。
- 排除 `/health/*`、`/openapi`、`/scalar` 与非 `/api` 路径。
