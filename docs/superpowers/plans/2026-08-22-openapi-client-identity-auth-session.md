# Identity Auth Session OpenAPI Client Generation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将登录、刷新、登出、偏好语言与会话加载路径迁移到 OpenAPI 生成客户端，并在 `packages/client-contracts` 薄适配层保留 CSRF 与 `retryUnauthorized: false`。

**Architecture:** 先补齐公开端点 OpenAPI `security: []`、manifest `publicOperationIds`、生成器 `RequestOptions` 透传与 HttpClient `headers`；再生成 `identity-auth-session` 四操作并薄适配 `identity-session.ts`。

**Tech Stack:** ASP.NET Core Minimal APIs + OpenAPI, TypeScript OpenAPI codegen, Vitest, Vue Pinia session store.

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-identity-auth-session-2026-08-22.md`](../verification/openapi-client-identity-auth-session-2026-08-22.md)。

---

### Task 1: Endpoint metadata + AllowAnonymous security[]

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/AuthEndpoints.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/OpenApi/FullNetOpenApiExtensions.cs`
- Modify: Architecture OpenApi tests + fixtures
- Test: `pnpm test:inner -- --snapshot openapi-client-identity-auth-session-metadata-20260822`

### Task 2: publicOperationIds + generator RequestOptions + snapshot/generate

**Files:**
- Modify: `packages/openapi/fullnet.openapi.v1.client-generation.json`
- Modify: readiness / snapshot / generate scripts
- Modify: `packages/codegen/src/openapi-client-generator.ts`
- Modify: `packages/http-client/src/index.ts`
- Generate: `packages/generated-clients/src/identity/identityAuthSession.ts`

### Task 3: Thin-adapt identity-session

**Files:**
- Modify: `packages/client-contracts/src/identity-session.ts`
- Modify: related Vitest suites

### Task 4: Full gates + dual-db slice + Verification

**Files:**
- Modify: manifest `identity-auth-session` → `generated`
- Modify: `docs/roadmap/openapi-client-generation.md`
- Create: `docs/superpowers/verification/2026-08-22-openapi-client-identity-auth-session-verification.md`
