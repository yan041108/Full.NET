# OpenAPI 客户端迁移：Code Generation Runs

**Goal:** 将 `ui/admin/src/api/code-generation-runs.ts` 迁入 OpenAPI 生成客户端（`code-generation-runs`）。

**Architecture:** 主 Tag `CodeGenerationRuns`；手写守卫与 `executeTrackedCodeGenerationRollback` 编排保留。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `codeGenerationPreviewRun` | `previewTrackedCodeGeneration` |
| `codeGenerationApplyRun` | `applyTrackedCodeGeneration` |
| `codeGenerationRollbackRun` | `rollbackTrackedCodeGeneration` |
| `codeGenerationRollbackRunChain` | `rollbackChainTrackedCodeGeneration` |
| `codeGenerationListRuns` | `listCodeGenerationRuns` |
| `codeGenerationDownloadRunArtifacts` | `downloadCodeGenerationArtifacts` |

清单 181→187（6 条 pilot）。
