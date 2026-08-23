# OpenAPI 客户端迁移：Organization Host User Management

**Goal:** 将 `ui/admin/src/api/host-user-organization-reference.ts` 迁入 OpenAPI 生成客户端（`organization-host-user-management`）。

**Architecture:** 主 Tag `OrganizationHostUserManagement`；手写 `isHostUserOrganizationReference` 保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-organization-host-user-management-2026-08-22.md`](../verification/openapi-client-organization-host-user-management-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `organizationGetHostUserManagementReference` | `getHostUserOrganizationReference` |
| `organizationCreateHostUserManagementUserUnit` | `createHostUserOrganizationUnit` |
| `organizationUpdateHostUserManagementUserUnit` | `updateHostUserOrganizationUnit` |
| `organizationDisableHostUserManagementUserUnit` | `disableHostUserOrganizationUnit` |
| `organizationCreateHostUserManagementUserPosition` | `createHostUserOrganizationPosition` |
| `organizationUpdateHostUserManagementUserPosition` | `updateHostUserOrganizationPosition` |
| `organizationDisableHostUserManagementUserPosition` | `disableHostUserOrganizationPosition` |

清单 108→115（7 条 pilot）。
