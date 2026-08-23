# OpenAPI 客户端迁移：Document Host Tags

**Goal:** 将 `ui/admin/src/api/host-document-tags.ts` 迁入 OpenAPI 生成客户端（`document-host-tags`）。

**Architecture:** 主 Tag `DocumentHostTags`；手写守卫与多参数创建/更新签名保留。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `documentHostListTags` | `listDocumentTags` |
| `documentHostCreateTag` | `createDocumentTag` |
| `documentHostUpdateTag` | `updateDocumentTag` |
| `documentHostDeleteTag` | `deleteDocumentTag` |

清单 216→220（4 条 pilot）。
