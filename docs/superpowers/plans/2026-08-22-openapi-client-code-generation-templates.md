# OpenAPI 客户端迁移：Code Generation Templates

**Goal:** 将 `ui/admin/src/api/code-generation-templates.ts` 迁入 OpenAPI 生成客户端（`code-generation-templates`）。

**Architecture:** 主 Tag `CodeGenerationTemplates`；手写守卫保留。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `codeGenerationListTemplates` | `listCodeGenerationTemplates` |
| `codeGenerationGetTemplate` | `getCodeGenerationTemplate` |
| `codeGenerationCreateTemplate` | `createCodeGenerationTemplate` |
| `codeGenerationUpdateTemplate` | `updateCodeGenerationTemplate` |
| `codeGenerationDeleteTemplate` | `deleteCodeGenerationTemplate` |

清单 187→192（5 条 pilot）。
