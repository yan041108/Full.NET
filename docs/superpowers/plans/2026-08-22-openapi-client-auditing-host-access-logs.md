# OpenAPI 客户端迁移：Auditing Host Access Logs

**Goal:** 将 `ui/admin/src/api/access-logs.ts` 迁入 OpenAPI 生成客户端（`auditing-host-access-logs`）。

**Architecture:** 主 Tag `AuditingHostAccessLogs`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-auditing-host-access-logs-2026-08-22.md`](../verification/openapi-client-auditing-host-access-logs-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `auditingListHostAccessLogs` | `listAuditingAccessLogs` |
| `auditingListHostAccessLogsByCursor` | `listAuditingAccessLogsByCursor` |

清单 146→148（2 条 pilot）。
