# OpenAPI 客户端迁移：Organization Tenant Units 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`organization-tenant-units`（`ui/admin/src/api/org-units.ts`）
- 计划：[`2026-08-22-openapi-client-organization-tenant-units.md`](../superpowers/plans/2026-08-22-openapi-client-organization-tenant-units.md)
- 比较基线：`dde01b32`（Tenancy Host Tenants 验证提交；含未提交的 Host Tenant Packages 工作区变更）
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Organization 首切片（Tenant Units）已通过完整门禁。5 个 Operation 具备稳定 `operationId` 与主 Tag `OrganizationTenantUnits`（含 201 Created 与 GET by id）；标准快照与生成物零漂移，`org-units.ts` 已收缩为薄适配层并保留手写 `isOrganizationUnit`/`isOrganizationUnitPage`。清单由 `pilot` 提升为 `generated`（现共 86 条）。

下一默认项为独立计划 **`org-positions.ts`**；禁止并行迁移其他资源组，禁止修改 `ui/admin-layui`。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `organizationListTenantUnits` | `listOrganizationUnits` |
| `organizationGetTenantUnit` | （仅生成） |
| `organizationCreateTenantUnit` | `createOrganizationUnit` |
| `organizationUpdateTenantUnit` | `updateOrganizationUnit` |
| `organizationDisableTenantUnit` | `disableOrganizationUnit` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 111/111，通过 |
| `pnpm --filter @fullnet/client-contracts test` | 138/138，通过 |
| `npx vitest run src/api/org-units.test.ts` | 2/2，通过 |
| `pnpm test:integration:affected -- --base dde01b32 --phase slice` | Organization+Tenancy 22/22，双 Provider，通过 |

## 边界与未验证项

- 页面导出签名未改；`parentId`/`displayOrder` 默认值行为保持不变。
- 手写 `isOrganizationUnit` 仍比生成守卫宽松（如 code 模式）；`organizationGetTenantUnit` 已生成但未新增 Vue 导出。
- 未迁移 `org-user-units` / `org-positions` 等；未修改 `ui/admin-layui`。

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
