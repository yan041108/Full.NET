# OpenAPI 客户端迁移：Organization Tenant User Units

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 `ui/admin/src/api/org-user-units.ts` 迁入 OpenAPI 生成客户端（`organization-tenant-user-units`）。

**Architecture:** 延续 ADR-0007 单模块 slice；主 Tag `OrganizationTenantUserUnits`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-organization-tenant-user-units-2026-08-22.md`](../verification/openapi-client-organization-tenant-user-units-2026-08-22.md)。

---

| operationId | Vue 导出 |
| --- | --- |
| `organizationListAssignableTenantUserUnitUsers` | `listAssignableOrganizationUserUnitUsers` |
| `organizationListTenantUserUnits` | `listOrganizationUserUnits` |
| `organizationCreateTenantUserUnit` | `createOrganizationUserUnit` |
| `organizationUpdateTenantUserUnit` | `updateOrganizationUserUnit` |
| `organizationDisableTenantUserUnit` | `disableOrganizationUserUnit` |

清单 86→91（5 条 pilot）。
