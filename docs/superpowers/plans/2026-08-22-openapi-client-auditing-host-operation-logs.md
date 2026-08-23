# OpenAPI 客户端迁移：Auditing Host Operation Logs

**Goal:** 将 `ui/admin/src/api/operation-logs.ts` 迁入 OpenAPI 生成客户端（`auditing-host-operation-logs`）。

**Architecture:** 主 Tag `AuditingHostOperationLogs`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-auditing-host-operation-logs-2026-08-22.md`](../verification/openapi-client-auditing-host-operation-logs-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `auditingListHostOperationLogs` | `listAuditingOperationLogs` |

清单 148→149（1 条 pilot）。
