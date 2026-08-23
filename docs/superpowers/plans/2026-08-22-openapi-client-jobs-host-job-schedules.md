# OpenAPI 客户端迁移：Jobs Host Job Schedules

**Goal:** 将 `ui/admin/src/api/host-job-schedules.ts` 迁入 OpenAPI 生成客户端（`jobs-host-job-schedules`）。

**Architecture:** 主 Tag `JobsHostJobSchedules`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-jobs-host-job-schedules-2026-08-22.md`](../verification/openapi-client-jobs-host-job-schedules-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `jobsListHostJobSchedules` | `listHostJobSchedules` |
| `jobsListHostJobScheduleDefinitionOptions` | `listHostJobScheduleDefinitionOptions` |
| `jobsPreviewHostJobScheduleCron` | `previewHostJobScheduleCron` |
| `jobsCreateHostJobSchedule` | `createHostJobSchedule` |
| `jobsUpdateHostJobSchedule` | `updateHostJobSchedule` |
| `jobsPauseHostJobSchedule` | `pauseHostJobSchedule` |
| `jobsResumeHostJobSchedule` | `resumeHostJobSchedule` |
| `jobsDeleteHostJobSchedule` | `deleteHostJobSchedule` |

清单 162→170（8 条 pilot）。
