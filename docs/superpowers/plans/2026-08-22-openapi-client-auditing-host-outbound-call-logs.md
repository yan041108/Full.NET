# OpenAPI 客户端迁移：Auditing Host Outbound Call Logs

**Goal:** 将 `ui/admin/src/api/outbound-call-logs.ts` 迁入 OpenAPI 生成客户端（`auditing-host-outbound-call-logs`）。

**Architecture:** 主 Tag `AuditingHostOutboundCallLogs`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-auditing-host-outbound-call-logs-2026-08-22.md`](../verification/openapi-client-auditing-host-outbound-call-logs-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `auditingListHostOutboundCallLogs` | `listAuditingOutboundCallLogs` |

清单 150→151（1 条 pilot）。
