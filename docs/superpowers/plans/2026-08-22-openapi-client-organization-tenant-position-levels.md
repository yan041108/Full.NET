# OpenAPI 客户端迁移：Organization Tenant Position Levels

**Goal:** 将 `ui/admin/src/api/org-position-levels.ts` 迁入 OpenAPI 生成客户端（`organization-tenant-position-levels`）。

**Architecture:** 主 Tag `OrganizationTenantPositionLevels`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-organization-tenant-position-levels-2026-08-22.md`](../verification/openapi-client-organization-tenant-position-levels-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `organizationListTenantPositionLevels` | `listOrganizationPositionLevels` |
| `organizationGetTenantPositionLevel` | （仅生成） |
| `organizationCreateTenantPositionLevel` | `createOrganizationPositionLevel` |
| `organizationUpdateTenantPositionLevel` | `updateOrganizationPositionLevel` |
| `organizationDisableTenantPositionLevel` | `disableOrganizationPositionLevel` |

清单 98→103（5 条 pilot）。
