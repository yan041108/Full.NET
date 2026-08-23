# OpenAPI 客户端迁移：Document Permissions

**Goal:** 将 `ui/admin/src/api/document-permissions.ts` 迁入 OpenAPI 生成客户端（`document-host-permissions`）。

**Architecture:** 主 Tag `DocumentHostPermissions`；请求体验证与守卫保留在薄适配层。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `documentHostListDocumentPermissions` | `getDocumentPermissionsByDocument` |
| `documentHostSetDocumentPermissions` | `setDocumentPermissions` |

清单 220→222（2 条 pilot）。
