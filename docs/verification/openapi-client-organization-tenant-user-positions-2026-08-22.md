# OpenAPI 客户端迁移：Organization Tenant User Positions 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`organization-tenant-user-positions`（`ui/admin/src/api/org-user-positions.ts`）
- 计划：[`2026-08-22-openapi-client-organization-tenant-user-positions.md`](../superpowers/plans/2026-08-22-openapi-client-organization-tenant-user-positions.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Organization 第五切片（Tenant User Positions）已通过完整门禁。5 个 Operation 具备稳定 `operationId` 与主 Tag `OrganizationTenantUserPositions`；`org-user-positions.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 108 条）。

Organization 模块 Vue `src/api` 租户侧资源组已全部迁入生成客户端。下一默认项为独立计划 **`host-user-organization-reference.ts`**（Host 用户组织引用）。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `organizationListAssignableTenantUserPositionUsers` | `listAssignableOrganizationUserPositionUsers` |
| `organizationListTenantUserPositions` | `listOrganizationUserPositions` |
| `organizationCreateTenantUserPosition` | `createOrganizationUserPosition` |
| `organizationUpdateTenantUserPosition` | `updateOrganizationUserPosition` |
| `organizationDisableTenantUserPosition` | `disableOrganizationUserPosition` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 111/111，通过 |
| `npx vitest run src/api/org-user-positions.test.ts` | 2/2，通过 |
| `pnpm test:integration:affected -- --base dde01b32 --phase slice` | Organization+Tenancy 22/22，双 Provider，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
