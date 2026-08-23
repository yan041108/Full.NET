# OpenAPI 客户端迁移：Serial Number Rules

**Goal:** 将 `ui/admin/src/api/serial-number-rules.ts` 迁入 OpenAPI 生成客户端（`serial-numbers-rules`）。

**Architecture:** 主 Tag `SerialNumbersHostRules`；手写守卫与列表查询参数构建保留。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `serialNumbersListRules` | `listSerialNumberRules` |
| `serialNumbersCreateRule` | `createSerialNumberRule` |
| `serialNumbersUpdateRule` | `updateSerialNumberRule` |
| `serialNumbersEnableRule` | `enableSerialNumberRule` |
| `serialNumbersDisableRule` | `disableSerialNumberRule` |
| `serialNumbersPreviewSerialNumber` | `previewSerialNumber` |

清单 195→201（6 条 pilot）。
