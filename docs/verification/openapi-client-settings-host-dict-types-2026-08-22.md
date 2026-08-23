# OpenAPI 客户端迁移：Settings Host Dict Types 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`settings-host-dict-types`（`ui/admin/src/api/dict-types.ts`）
- 计划：[`2026-08-22-openapi-client-settings-host-dict-types.md`](../superpowers/plans/2026-08-22-openapi-client-settings-host-dict-types.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Settings Host Dict Types 切片已通过完整门禁。13 个 Operation 具备稳定 `operationId` 与主 Tag `SettingsHostDictTypes`；`dict-types.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 131 条）。

下一默认项为 Settings 其余 `src/api` 单模块 slice（如 `tenant-dict-types.ts`）；禁止并行批量改写。

## 范围

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

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 111/111，通过 |
| `npx vitest run src/api/dict-types.test.ts` | 4/4，通过 |
| `pnpm test:integration:affected -- --base dde01b32 --phase slice` | Organization+Settings+Tenancy 34/34，双 Provider，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
