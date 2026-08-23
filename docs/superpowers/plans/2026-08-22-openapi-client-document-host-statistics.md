# OpenAPI 客户端迁移：Document Statistics

**Goal:** 将 `ui/admin/src/api/document-statistics.ts` 迁入 OpenAPI 生成客户端（`document-host-statistics`）。

**Architecture:** 主 Tag `DocumentHostStatistics`；单 GET 汇总端点。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `documentHostGetDocumentStatistics` | `getDocumentStatistics` |

清单 229→230（1 条 pilot）。
