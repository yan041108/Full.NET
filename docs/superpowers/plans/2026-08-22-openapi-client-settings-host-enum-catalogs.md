# OpenAPI 客户端迁移：Settings Host Enum Catalogs

**Goal:** 将 `ui/admin/src/api/enum-catalogs.ts` 迁入 OpenAPI 生成客户端（`settings-host-enum-catalogs`）。

**Architecture:** 主 Tag `SettingsHostEnumCatalogs`；手写 `isSettingsEnumCatalogSummary`/`isSettingsEnumCatalogDetail` 保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-settings-host-enum-catalogs-2026-08-22.md`](../verification/openapi-client-settings-host-enum-catalogs-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `settingsListHostEnumCatalogs` | `listSettingsEnumCatalogs` |
| `settingsGetHostEnumCatalog` | `getSettingsEnumCatalog` |

清单 131→133（2 条 pilot）。
