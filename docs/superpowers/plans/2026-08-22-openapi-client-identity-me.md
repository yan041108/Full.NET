# OpenAPI 客户端迁移：Identity Me（Identity remaining 第 6 slice）

> **For agentic workers:** 按本计划逐步执行；每 Task 独立 snapshot；行为变更必须 RED→GREEN。

**Goal:** 将 `ui/admin/src/api/me.ts` 收缩为 OpenAPI 生成 Operation 薄适配层（`identity-me`）。

**Architecture:** 延续 ADR-0007 与既有 Identity remaining slice 模式。夹具历史 `operationId: getCurrentUser` 收敛为 `identityGetCurrentUser`。

**Approved basis:** ADR-0007；Module Catalog [`Slice-passed`](../../verification/openapi-client-identity-host-modules-2026-08-22.md)。

**Baseline:** `cbbf807a27a3e3f11eaf34af77fc1b567bbcb5ce`。

## 执行状态（2026-08-22）

- 计划已创建；实现进行中。

## Global Constraints

- 唯一 `generatedGroup`：`identity-me`。
- 仅覆盖 `GET /api/v1/me`；不纳入 `/api/v1/me/locale`、TOTP 或 auth/session。
- `ui/admin-layui/**` 零修改；禁止改路径/方法/状态码/序列化形状。
- 只允许补齐 `WithName`、主 Tag、`Produces` / ProblemDetails，以及夹具 `operationId` 收敛。
- 无独立 Vue test：Task 3 必须先补 `me.test.ts` RED，再薄适配。
- 禁止并行其他资源组。

## 目标 Operation

主 Tag：`IdentityMe`。

| Method | Path | operationId | Vue 导出 |
| --- | --- | --- | --- |
| GET | `/api/v1/me` | `identityGetCurrentUser` | `getCurrentUser` |

---

### Task 1
**Snapshot:** `openapi-client-identity-me-metadata-20260822`  
→ 提交 `feat: stabilize Identity Me OpenAPI identities`

### Task 2
**Snapshot:** `openapi-client-identity-me-snapshot-20260822`  
1 条 `pilot`；58→59 → 提交 `feat: freeze Identity Me client OpenAPI snapshot`

### Task 3
**Snapshot:** `openapi-client-identity-me-generate-20260822`  
先补 RED 单测，再薄适配 → 提交 `feat: generate Identity Me client operations`

### Task 4
**Snapshot:** `openapi-client-identity-me-verify-20260822`  
完整门禁 + 双库 slice → `Slice-passed` → `generated` → 提交 `docs: verify Identity Me OpenAPI client slice`

## 停止条件

继承 ADR-0007；失败保持 `pilot`。

## 规则与 Skill 复盘预期

无新冲突则 Verification 写一行“未新增规则/Skill 候选”。
