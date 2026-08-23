# OpenAPI 客户端迁移：Tenancy Host Tenant Packages

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 `ui/admin/src/api/tenant-packages.ts` 迁入 OpenAPI 生成客户端（`tenancy-host-tenant-packages`）。

**Architecture:** 延续 ADR-0007 单模块 slice；主 Tag `TenancyHostTenantPackages`；手写 `isHostTenantPackage`/`isHostTenantPackagePage` 保留。

**Tech Stack:** ASP.NET Core Minimal APIs + OpenAPI, TypeScript OpenAPI codegen, Vitest, Vue admin.

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-tenancy-host-tenant-packages-2026-08-22.md`](../verification/openapi-client-tenancy-host-tenant-packages-2026-08-22.md)。

---

### Task 1: Endpoint metadata + Architecture

**Files:** `ManageHostTenantPackages/Endpoint.cs`, Architecture tests, openapi contract test

锁定 5 个 Operation：

| operationId | HTTP | Vue 导出 |
| --- | --- | --- |
| `tenancyListHostTenantPackages` | GET list | `listHostTenantPackages` |
| `tenancyGetHostTenantPackage` | GET by id | （仅生成） |
| `tenancyCreateHostTenantPackage` | POST create 201 | `createHostTenantPackage` |
| `tenancyUpdateHostTenantPackage` | PUT update | `updateHostTenantPackage` |
| `tenancyDisableHostTenantPackage` | POST disable | `disableHostTenantPackage` |

### Task 2: Snapshot + pilot + generate

**Files:** manifest, fullnet-client-v1.openapi.json, generated clients

清单 76→81（5 条 pilot）。

### Task 3: Thin-adapt tenant-packages.ts

**Files:** `ui/admin/src/api/tenant-packages.ts`, `tenant-packages.test.ts`

### Task 4: Full gates + dual-db slice + Verification

Promote `pilot` → `generated` (76→81).
