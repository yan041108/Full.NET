# OpenAPI 客户端迁移：Identity Host Modules（Identity remaining 第 5 slice）

> **For agentic workers:** 按本计划逐步执行；每 Task 独立 snapshot；行为变更必须 RED→GREEN。

**Goal:** 将 `ui/admin/src/api/module-catalog.ts` 收缩为 OpenAPI 生成 Operation 薄适配层（`identity-host-modules`）。

**Architecture:** 延续 ADR-0007 与既有 Identity remaining slice 模式。

**Approved basis:** ADR-0007；Online Sessions [`Slice-passed`](../../verification/openapi-client-identity-host-online-sessions-2026-08-22.md)。

**Baseline:** `0c5d1af3357d10732aba167834aac3a86c0b3d9c`。

## 执行状态（2026-08-22）

- `Slice-passed`：见 [`openapi-client-identity-host-modules-2026-08-22.md`](../../verification/openapi-client-identity-host-modules-2026-08-22.md)。
- 允许创建下一个 Identity remaining 计划（默认 `me.ts`）；禁止并行迁移其他资源组。

## Global Constraints

- 唯一 `generatedGroup`：`identity-host-modules`。
- `ui/admin-layui/**` 零修改；禁止改路径/方法/状态码/序列化形状。
- 只允许补齐 `WithName`、主 Tag、ProblemDetails；列表夹具可补 `ModuleCatalogEntryResponseArray`。
- 无独立 Vue test：Task 3 必须先补 `module-catalog.test.ts` RED，再薄适配。
- 禁止并行其他资源组。

## 目标 Operation

主 Tag：`IdentityHostModules`。

| Method | Path | operationId | Vue 导出 |
| --- | --- | --- | --- |
| GET | `/api/v1/identity/modules` | `identityListHostModules` | `listIdentityModules` |
| GET | `/api/v1/identity/modules/{moduleKey}` | `identityGetHostModule` | `getIdentityModule` |

---

### Task 1
**Snapshot:** `openapi-client-identity-host-modules-metadata-20260822`  
→ 提交 `feat: stabilize Identity Host Modules OpenAPI identities`

### Task 2
**Snapshot:** `openapi-client-identity-host-modules-snapshot-20260822`  
2 条 `pilot`；56→58 → 提交 `feat: freeze Identity Host Modules client OpenAPI snapshot`

### Task 3
**Snapshot:** `openapi-client-identity-host-modules-generate-20260822`  
先补 RED 单测，再薄适配 → 提交 `feat: generate Identity Host Modules client operations`

### Task 4
**Snapshot:** `openapi-client-identity-host-modules-verify-20260822`  
完整门禁 + 双库 slice → `Slice-passed` → `generated` → 提交 `docs: verify Identity Host Modules OpenAPI client slice`

## 停止条件

继承 ADR-0007；失败保持 `pilot`。

## 规则与 Skill 复盘预期

无新冲突则 Verification 写一行“未新增规则/Skill 候选”。
