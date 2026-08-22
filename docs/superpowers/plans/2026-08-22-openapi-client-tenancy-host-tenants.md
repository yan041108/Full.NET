# OpenAPI 客户端迁移：Tenancy Host Tenants

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 `ui/admin/src/api/tenants.ts` 迁入 OpenAPI 生成客户端（`tenancy-host-tenants`）。

**Architecture:** 延续 ADR-0007 单模块 slice；主 Tag `TenancyHostTenants`；手写 `isHostTenant`/`isHostTenantPage` 保留。

**Tech Stack:** ASP.NET Core Minimal APIs + OpenAPI, TypeScript OpenAPI codegen, Vitest, Vue admin.

---

### Task 1: Endpoint metadata + Architecture

**Files:** `ManageHostTenants/Endpoint.cs`, Architecture tests, openapi contract test

### Task 2: Snapshot + pilot + generate

**Files:** manifest, fullnet-client-v1.openapi.json, generated clients

### Task 3: Thin-adapt tenants.ts

**Files:** `ui/admin/src/api/tenants.ts`, `tenants.test.ts`

### Task 4: Full gates + dual-db slice + Verification

Promote `pilot` → `generated` (70→76).
