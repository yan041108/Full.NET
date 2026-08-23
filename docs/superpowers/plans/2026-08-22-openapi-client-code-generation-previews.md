# OpenAPI 客户端迁移：Code Generation Previews

**Goal:** 将 `ui/admin/src/api/code-generation-previews.ts` 迁入 OpenAPI 生成客户端（`code-generation-previews`）。

**Architecture:** 主 Tag `CodeGenerationPreviews`；手写守卫保留。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `codeGenerationPreviewCrud` | `previewCodeGeneration` |

清单 180→181（1 条 pilot）。
