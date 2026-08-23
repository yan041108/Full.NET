# OpenAPI 客户端迁移：Organization Tenant Positions

**Goal:** 将 `ui/admin/src/api/org-positions.ts` 迁入 OpenAPI 生成客户端（`organization-tenant-positions`）。

**Architecture:** 主 Tag `OrganizationTenantPositions`；手写 `isOrganizationPosition`/`isOrganizationPositionPage` 保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-organization-tenant-positions-2026-08-22.md`](../verification/openapi-client-organization-tenant-positions-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `organizationListTenantPositions` | `listOrganizationPositions` |
| `organizationGetTenantPosition` | （仅生成） |
| `organizationCreateTenantPosition` | `createOrganizationPosition` |
| `organizationUpdateTenantPosition` | `updateOrganizationPosition` |
| `organizationAssignTenantPositionUnit` | `assignOrganizationPositionUnit` |
| `organizationAssignTenantPositionLevel` | `assignOrganizationPositionLevel` |
| `organizationDisableTenantPosition` | `disableOrganizationPosition` |

清单 91→98（7 条 pilot）。
