# OpenAPI 客户端迁移：Auditing Host Exception Logs

**Goal:** 将 `ui/admin/src/api/exception-logs.ts` 迁入 OpenAPI 生成客户端（`auditing-host-exception-logs`）。

**Architecture:** 主 Tag `AuditingHostExceptionLogs`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-auditing-host-exception-logs-2026-08-22.md`](../verification/openapi-client-auditing-host-exception-logs-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `auditingListHostExceptionLogs` | `listAuditingExceptionLogs` |

清单 149→150（1 条 pilot）。
