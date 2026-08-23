# OpenAPI 客户端迁移：Platform Host Dashboard

**Goal:** 将 `ui/admin/src/api/platform-dashboard.ts` 迁入 OpenAPI 生成客户端（`platform-host-dashboard`）。

**Architecture:** 主 Tag `PlatformHostDashboard`；手写 `isHostDashboardSummary` 保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-platform-host-dashboard-2026-08-22.md`](../verification/openapi-client-platform-host-dashboard-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `platformGetHostDashboardSummary` | `getHostDashboardSummary` |

清单 151→152（1 条 pilot）。
