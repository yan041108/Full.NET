# OpenAPI 客户端迁移：Jobs Host Jobs

**Goal:** 将 `ui/admin/src/api/host-jobs.ts` 迁入 OpenAPI 生成客户端（`jobs-host-jobs`）。

**Architecture:** 主 Tag `JobsHostJobDefinitions` / `JobsHostJobExecutions`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-jobs-host-jobs-2026-08-22.md`](../verification/openapi-client-jobs-host-jobs-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `jobsListHostJobDefinitions` | `listHostJobDefinitions` |
| `jobsListHostJobGroups` | `listHostJobGroups` |
| `jobsCreateHostJobDefinition` | `createHostJobDefinition` |
| `jobsUpdateHostJobDefinition` | `updateHostJobDefinition` |
| `jobsDisableHostJobDefinition` | `disableHostJobDefinition` |
| `jobsDeleteHostJobDefinition` | `deleteHostJobDefinition` |
| `jobsTriggerHostJobDefinition` | `triggerHostJobDefinition` |
| `jobsListHostJobExecutions` | `listHostJobExecutions` |
| `jobsGetHostJobExecution` | `getHostJobExecution` |
| `jobsClearHostJobExecutions` | `clearHostJobExecutions` |

清单 152→162（10 条 pilot）。
