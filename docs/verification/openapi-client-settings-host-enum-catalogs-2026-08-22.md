# OpenAPI 客户端迁移：Settings Host Enum Catalogs 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`settings-host-enum-catalogs`（`ui/admin/src/api/enum-catalogs.ts`）
- 计划：[`2026-08-22-openapi-client-settings-host-enum-catalogs.md`](../superpowers/plans/2026-08-22-openapi-client-settings-host-enum-catalogs.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Settings Host Enum Catalogs 切片已通过完整门禁。2 个 Operation 具备稳定 `operationId` 与主 Tag `SettingsHostEnumCatalogs`；`enum-catalogs.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 133 条）。

下一默认项为 Settings 其余 `src/api` 单模块 slice；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `settingsListHostEnumCatalogs` | `listSettingsEnumCatalogs` |
| `settingsGetHostEnumCatalog` | `getSettingsEnumCatalog` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 111/111，通过 |
| `npx vitest run src/api/enum-catalogs.test.ts` | 1/1，通过 |
| `pnpm test:integration:affected -- --base dde01b32 --phase slice` | Organization+Settings+Tenancy 34/34，双 Provider，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
