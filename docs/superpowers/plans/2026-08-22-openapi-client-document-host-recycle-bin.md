# OpenAPI 客户端迁移：Document Recycle Bin

**Goal:** 将 `ui/admin/src/api/document-recycle-bin.ts` 迁入 OpenAPI 生成客户端（`document-host-recycle-bin`）。

**Architecture:** 主 Tag `DocumentHostRecycleBin`；恢复请求体验证保留在薄适配层。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `documentHostListRecycleBinItems` | `listRecycleBinItems` |
| `documentHostRestoreRecycleBinItem` | `restoreRecycleBinItem` |
| `documentHostPurgeRecycleBinItem` | `purgeRecycleBinItem` |

清单 222→225（3 条 pilot）。
