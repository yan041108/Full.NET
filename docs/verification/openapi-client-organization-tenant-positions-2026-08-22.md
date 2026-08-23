# OpenAPI 客户端迁移：Organization Tenant Positions 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`organization-tenant-positions`（`ui/admin/src/api/org-positions.ts`）
- 计划：[`2026-08-22-openapi-client-organization-tenant-positions.md`](../superpowers/plans/2026-08-22-openapi-client-organization-tenant-positions.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Organization 第三切片（Tenant Positions）已通过完整门禁。7 个 Operation 具备稳定 `operationId` 与主 Tag `OrganizationTenantPositions`（含 assign unit/level 与 201 Created）；`org-positions.ts` 已收缩为薄适配层并保留手写守卫。清单由 `pilot` 提升为 `generated`（现共 98 条）。

下一默认项为独立计划 **`org-user-positions.ts`**；禁止并行迁移其他资源组。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `organizationListTenantPositions` | `listOrganizationPositions` |
| `organizationGetTenantPosition` | （仅生成） |
| `organizationCreateTenantPosition` | `createOrganizationPosition` |
| `organizationUpdateTenantPosition` | `updateOrganizationPosition` |
| `organizationAssignTenantPositionUnit` | `assignOrganizationPositionUnit` |
| `organizationAssignTenantPositionLevel` | `assignOrganizationPositionLevel` |
| `organizationDisableTenantPosition` | `disableOrganizationPosition` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 111/111，通过 |
| `npx vitest run src/api/org-positions.test.ts` | 4/4，通过 |
| `pnpm test:integration:affected -- --base dde01b32 --phase slice` | Organization+Tenancy 22/22，双 Provider，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
