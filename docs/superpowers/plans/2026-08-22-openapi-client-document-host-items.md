# OpenAPI 客户端迁移：Document Host Items

**Goal:** 将 `ui/admin/src/api/host-document-items.ts` 迁入 OpenAPI 生成客户端（`document-host-items`）。

**Architecture:** 主 Tag `DocumentHostItems`；blob 预览/下载使用 `application/octet-stream`；multipart 上传与 `openDocumentBlob` 保留在薄适配层。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `documentHostListItems` | `listDocumentItems` |
| `documentHostCreateItem` | `createDocumentItem` |
| `documentHostUpdateItem` | `updateDocumentItem` |
| `documentHostListItemVersions` | `listDocumentVersions` |
| `documentHostAddItemVersion` | `addDocumentVersion` |
| `documentHostUploadItemVersion` | `uploadDocumentVersion` |
| `documentHostDownloadItemContent` | `downloadDocumentContent` |
| `documentHostPreviewItemContent` | `previewDocumentContent`（无 versionId） |
| `documentHostPreviewItemVersionContent` | `previewDocumentContent`（有 versionId） |
| `documentHostDeleteItem` | `deleteDocumentItem` |
| `documentHostRestoreItem` | `restoreDocumentItem` |

清单 205→216（11 条 pilot）。
