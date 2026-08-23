# OpenAPI 客户端迁移：Organization Tenant User Positions

**Goal:** 将 `ui/admin/src/api/org-user-positions.ts` 迁入 OpenAPI 生成客户端（`organization-tenant-user-positions`）。

**Architecture:** 主 Tag `OrganizationTenantUserPositions`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-organization-tenant-user-positions-2026-08-22.md`](../verification/openapi-client-organization-tenant-user-positions-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `organizationListAssignableTenantUserPositionUsers` | `listAssignableOrganizationUserPositionUsers` |
| `organizationListTenantUserPositions` | `listOrganizationUserPositions` |
| `organizationCreateTenantUserPosition` | `createOrganizationUserPosition` |
| `organizationUpdateTenantUserPosition` | `updateOrganizationUserPosition` |
| `organizationDisableTenantUserPosition` | `disableOrganizationUserPosition` |

清单 103→108（5 条 pilot）。
