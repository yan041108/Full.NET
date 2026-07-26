# Jobs Host 任务定义验证（2026-07-26）

## 摘要

交付 Host 作用域任务定义创建/更新/禁用、手动触发同步执行、执行记录查询，以及 Worker 轮询处理器；双管理端 UI 与 OpenAPI/client-contracts 对齐。

| 维度 | 结果 |
| --- | --- |
| 迁移 | `030_JobsDefinitionAndExecution.sql` SQL Server + MySQL |
| API | `/api/v1/jobs/host-definitions`、`/api/v1/jobs/host-executions` |
| 权限 | `jobs.definitions.read/write`、`jobs.executions.read` |
| Integration 双库 | `Host_job_definition_and_trigger` SQL Server/MySQL **2/2**，含过期 `Running` 租约恢复 |
| OpenAPI | `jobs-host-definitions-v1.json`；离线契约 **2/2** |
| client-contracts | `host-jobs.ts` + Vitest |
| Mock parity | 「任务调度列表与触发」× 双端 **2/2** → `shell-parity` **60 → 62** |
| 双端 UI | `HostJobsView.vue` + `host-jobs.js` |
| 四处 canonical 门槛 | **359/7/40/172** |

## 手动验证建议

1. Migrator 应用 `030` 后，以 Host 管理员登录双管理端「任务调度」。
2. 创建 `jobs.ping` 任务并「立即执行」，执行记录状态应为 `succeeded`。
3. 禁用任务后再次触发应返回 `jobs.definition_disabled`。
4. Worker 异常退出后，超过租约期限的 `running` 执行应由下一实例重新领取，且尝试次数递增。

## 关联

- [实施计划](../superpowers/plans/2026-07-26-jobs-host-definitions-vertical-slice.md)
- [Admin.NET 对标矩阵](../roadmap/adminnet-feature-parity.md)
