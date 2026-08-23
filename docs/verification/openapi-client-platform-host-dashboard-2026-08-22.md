# OpenAPI 客户端迁移：Platform Host Dashboard 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`platform-host-dashboard`（`ui/admin/src/api/platform-dashboard.ts`）
- 计划：[`2026-08-22-openapi-client-platform-host-dashboard.md`](../superpowers/plans/2026-08-22-openapi-client-platform-host-dashboard.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Platform Host Dashboard 切片已通过完整门禁。1 个 Operation 具备稳定 `operationId` 与主 Tag `PlatformHostDashboard`；`platform-dashboard.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 152 条）。

下一默认项为其他模块单 slice（如 `host-jobs.ts`）；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `platformGetHostDashboardSummary` | `getHostDashboardSummary` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 通过 |
| `npx vitest run src/api/platform-dashboard.test.ts` | 1/1，通过 |
| `pnpm test:integration:affected -- --base dde01b32 --phase slice` | Auditing+Identity+Organization+Settings+Tenancy 76/76，双 Provider，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
