# OpenAPI 客户端迁移：Code Generation Catalog

**Goal:** 将 `ui/admin/src/api/code-generation-catalog.ts` 迁入 OpenAPI 生成客户端（`code-generation-catalog`）。

**Architecture:** 主 Tag `CodeGenerationCatalog`；手写守卫保留。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `codeGenerationListCatalogTables` | `listCodeGenerationCatalogTables` |
| `codeGenerationListCatalogColumns` | `listCodeGenerationCatalogColumns` |
| `codeGenerationSyncCatalogColumns` | `syncCodeGenerationCatalogColumns` |

清单 192→195（3 条 pilot）。
