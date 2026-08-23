# OpenAPI 客户端迁移：Organization Tenant User Units 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`organization-tenant-user-units`（`ui/admin/src/api/org-user-units.ts`）
- 计划：[`2026-08-22-openapi-client-organization-tenant-user-units.md`](../superpowers/plans/2026-08-22-openapi-client-organization-tenant-user-units.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Organization 第二切片（Tenant User Units）已通过完整门禁。5 个 Operation 具备稳定 `operationId` 与主 Tag `OrganizationTenantUserUnits`（含 assignable-users 与 201 Created）；标准快照与生成物零漂移，`org-user-units.ts` 已收缩为薄适配层并保留手写守卫。清单由 `pilot` 提升为 `generated`（现共 91 条）。

下一默认项为独立计划 **`org-position-levels.ts`**；禁止并行迁移其他资源组，禁止修改 `ui/admin-layui`。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `organizationListAssignableTenantUserUnitUsers` | `listAssignableOrganizationUserUnitUsers` |
| `organizationListTenantUserUnits` | `listOrganizationUserUnits` |
| `organizationCreateTenantUserUnit` | `createOrganizationUserUnit` |
| `organizationUpdateTenantUserUnit` | `updateOrganizationUserUnit` |
| `organizationDisableTenantUserUnit` | `disableOrganizationUserUnit` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 111/111，通过 |
| `npx vitest run src/api/org-user-units.test.ts` | 2/2，通过 |
| `pnpm test:integration:affected -- --base dde01b32 --phase slice` | Organization+Tenancy 22/22，双 Provider，通过 |

## 边界与未验证项

- 页面导出签名未改；`listOrganizationUserUnits` 仍只传 `page`/`pageSize`（生成 Operation 支持可选 `userId`/`unitId` 过滤）。
- 手写守卫保留；未迁移 `org-positions` 等；未修改 `ui/admin-layui`。

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
