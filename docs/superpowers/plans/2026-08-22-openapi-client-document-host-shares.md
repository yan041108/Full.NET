# OpenAPI 客户端迁移：Document Shares

**Goal:** 将 `ui/admin/src/api/document-shares.ts` 迁入 OpenAPI 生成客户端（`document-host-shares`）。

**Architecture:** Host 操作主 Tag `DocumentHostShares`；匿名访问主 Tag `DocumentPublicShares` 并登记 `publicOperationIds`。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `documentHostListDocumentShares` | `listDocumentShares` |
| `documentHostCreateDocumentShare` | `createDocumentShare` |
| `documentHostUpdateDocumentShareStatus` | `updateDocumentShareStatus` |
| `documentPublicAccessDocumentShare` | `accessDocumentShareByCode` |

清单 225→229（4 条 pilot）。
