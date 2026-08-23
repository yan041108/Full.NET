# OpenAPI 客户端迁移：Organization Tenant Units

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 `ui/admin/src/api/org-units.ts` 迁入 OpenAPI 生成客户端（`organization-tenant-units`）。

**Architecture:** 延续 ADR-0007 单模块 slice；主 Tag `OrganizationTenantUnits`；手写 `isOrganizationUnit`/`isOrganizationUnitPage` 保留。

**Tech Stack:** ASP.NET Core Minimal APIs + OpenAPI, TypeScript OpenAPI codegen, Vitest, Vue admin.

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-organization-tenant-units-2026-08-22.md`](../verification/openapi-client-organization-tenant-units-2026-08-22.md)。

---

### Task 1: Endpoint metadata + Architecture

| operationId | Vue 导出 |
| --- | --- |
| `organizationListTenantUnits` | `listOrganizationUnits` |
| `organizationGetTenantUnit` | （仅生成） |
| `organizationCreateTenantUnit` | `createOrganizationUnit` |
| `organizationUpdateTenantUnit` | `updateOrganizationUnit` |
| `organizationDisableTenantUnit` | `disableOrganizationUnit` |

### Task 2: Snapshot + pilot + generate

清单 81→86（5 条 pilot）。

### Task 3: Thin-adapt org-units.ts

### Task 4: Full gates + dual-db slice + Verification

Promote `pilot` → `generated` (81→86).
