# OpenAPI 客户端迁移：Settings Host Dict Types

**Goal:** 将 `ui/admin/src/api/dict-types.ts` 迁入 OpenAPI 生成客户端（`settings-host-dict-types`）。

**Architecture:** 主 Tag `SettingsHostDictTypes`；手写 `isSettingsDictType`/`isSettingsDictItem` 等守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-settings-host-dict-types-2026-08-22.md`](../verification/openapi-client-settings-host-dict-types-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `settingsListHostDictTypes` | `listSettingsDictTypes` |
| `settingsCreateHostDictType` | `createSettingsDictType` |
| `settingsUpdateHostDictType` | `updateSettingsDictType` |
| `settingsDisableHostDictType` | `disableSettingsDictType` |
| `settingsDeleteHostDictType` | `deleteSettingsDictType` |
| `settingsListAllHostDictTypes` | `listAllSettingsDictTypes` |
| `settingsListHostDictItemsByTypeCode` | `listSettingsDictItemsByCode` |
| `settingsListHostDictItems` | `listSettingsDictItems` |
| `settingsCreateHostDictItem` | `createSettingsDictItem` |
| `settingsGetHostDictItem` | `getSettingsDictItem` |
| `settingsUpdateHostDictItem` | `updateSettingsDictItem` |
| `settingsDisableHostDictItem` | `disableSettingsDictItem` |
| `settingsDeleteHostDictItem` | `deleteSettingsDictItem` |

清单 118→131（13 条 pilot）。
