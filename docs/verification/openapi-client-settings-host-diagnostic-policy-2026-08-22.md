# OpenAPI 客户端迁移：Settings Host Diagnostic Policy 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`settings-host-diagnostic-policy`（`ui/admin/src/api/diagnostic-policy.ts`）
- 计划：[`2026-08-22-openapi-client-settings-host-diagnostic-policy.md`](../superpowers/plans/2026-08-22-openapi-client-settings-host-diagnostic-policy.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Settings/Auditing 队列首个切片（Host Diagnostic Policy）已通过完整门禁。3 个 Operation 具备稳定 `operationId` 与主 Tag `SettingsHostDiagnosticPolicy`；`diagnostic-policy.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 118 条）。

下一默认项为 Settings 模块其余 `src/api` 单模块 slice（如 `dict-types.ts`）；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `settingsGetHostDiagnosticPolicy` | `getDiagnosticPolicy` |
| `settingsUpdateHostDiagnosticPolicy` | `updateDiagnosticPolicy` |
| `settingsRestoreHostDiagnosticPolicy` | `restoreDiagnosticPolicy` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 111/111，通过 |
| `npx vitest run src/api/diagnostic-policy.test.ts` | 1/1，通过 |
| `pnpm test:integration:affected -- --base dde01b32 --phase slice` | Organization+Settings+Tenancy 34/34，双 Provider，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
