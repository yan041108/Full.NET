# OpenAPI 客户端迁移：Jobs Host Job Health

**Goal:** 将 `ui/admin/src/api/host-job-health.ts` 迁入 OpenAPI 生成客户端（`jobs-host-job-health`）。

**Architecture:** 主 Tag `JobsHostJobHealth`；手写 `isHostJobHealth` 保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-jobs-host-job-health-2026-08-22.md`](../verification/openapi-client-jobs-host-job-health-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `jobsGetHostJobHealth` | `getHostJobHealth` |

清单 170→171（1 条 pilot）。
