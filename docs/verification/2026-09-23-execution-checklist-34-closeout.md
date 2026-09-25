# 执行清单 34 — Jobs 批量暂停/恢复 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **34**（`batch-pause` / `batch-resume`、逐项结果与乐观锁版本）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `POST /api/v1/jobs/host-schedules/batch-pause`、`batch-resume` |
| 服务 | `HostJobScheduleService.BatchPauseAsync` / `BatchResumeAsync` |
| 权限 | `jobs.schedules.pause`、`jobs.schedules.resume` |
| Vue | `HostJobSchedulesView.vue`（`host-job-schedules-batch-pause`） |
| 集成 | `JobsScheduleBatchAssertions` |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`HostJobScheduleBatchOperationsTests` | 批量边界 |
| real-stack | `host-job-schedules-batch.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
