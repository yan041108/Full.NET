# 执行清单 33 — Jobs 执行取消 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **33**（`POST …/host-executions/{id}/cancel`、pending→cancelled、running→cancelling、终态 409）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `jobsCancelHostJobExecution` |
| 服务 | `HostJobExecutionCancelService` |
| 权限 | `jobs.executions.cancel` |
| Vue | `HostJobExecutionsView.vue`（`host-job-executions-cancel`） |
| 集成 | `JobsExecutionCancelAssertions` |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`HostJobExecutionCancelServiceTests` | pending/终态冲突 |
| real-stack | `host-job-executions-cancel.spec.mjs`（竞态下取消或 409） |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
