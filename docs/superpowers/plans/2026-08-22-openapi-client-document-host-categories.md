# OpenAPI 客户端迁移：Document Host Categories

**Goal:** 将 `ui/admin/src/api/host-document-categories.ts` 迁入 OpenAPI 生成客户端（`document-host-categories`）。

**Architecture:** 主 Tag `DocumentHostCategories`；手写守卫与多参数创建/更新签名保留。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `documentHostListCategories` | `listDocumentCategories` |
| `documentHostCreateCategory` | `createDocumentCategory` |
| `documentHostUpdateCategory` | `updateDocumentCategory` |
| `documentHostDeleteCategory` | `deleteDocumentCategory` |

清单 201→205（4 条 pilot）。
