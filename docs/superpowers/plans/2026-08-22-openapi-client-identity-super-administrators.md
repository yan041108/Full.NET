# OpenAPI 客户端迁移：Identity Super Administrators（Identity remaining 第 8 slice）

> **For agentic workers:** 按本计划逐步执行；每 Task 独立 snapshot；行为变更必须 RED→GREEN。

**Goal:** 将 `ui/admin/src/api/superAdministrators.ts` 收缩为 OpenAPI 生成 Operation 薄适配层（`identity-super-administrators`）。

**Architecture:** 延续 ADR-0007。本资源含 grant/revoke 敏感写操作与强重认证正文；只做客户端生成薄适配，不改变权限、速率限制或最后一名保护语义。

**Approved basis:** ADR-0007；TOTP Enrollment [`Slice-passed`](../../verification/openapi-client-identity-totp-enrollment-2026-08-22.md)；超管边界见 R-20260718-super-administrator-boundary。

**Baseline:** `c25061926b02d42ad60cde37c0806f614a4d2059`。

## 执行状态（2026-08-22）

- 计划已创建；实现进行中。

## Global Constraints

- 唯一 `generatedGroup`：`identity-super-administrators`。
- `ui/admin-layui/**` 零修改；禁止改路径/方法/状态码/序列化形状。
- 只允许补齐 `WithName`、主 Tag、`Produces` / ProblemDetails；保留 `RequireFullNetPermission` 与 `RequireRateLimiting`。
- 密码与可选 TOTP 仅经 JSON 正文传递；禁止改到 query/header。
- 既有 `superAdministrators.test.ts` 改为 mock `http` 并保持 GREEN。
- 禁止并行其他资源组；本 slice 单独 Verification。

## 目标 Operation

主 Tag：`IdentitySuperAdministrators`。

| Method | Path | operationId | Vue 导出 |
| --- | --- | --- | --- |
| GET | `/api/v1/identity/super-administrators` | `identityListSuperAdministrators` | `getSuperAdministrators` |
| GET | `/api/v1/identity/super-administrators/audits` | `identityListSuperAdministratorAudits` | `getSuperAdministratorAudits` |
| POST | `/api/v1/identity/super-administrators/grant` | `identityGrantSuperAdministrator` | `grantSuperAdministrator` |
| POST | `/api/v1/identity/super-administrators/{targetUserId}/revoke` | `identityRevokeSuperAdministrator` | `revokeSuperAdministrator` |

---

### Task 1
**Snapshot:** `openapi-client-identity-super-administrators-metadata-20260822`  
→ 提交 `feat: stabilize Identity Super Administrators OpenAPI identities`

### Task 2
**Snapshot:** `openapi-client-identity-super-administrators-snapshot-20260822`  
4 条 `pilot`；62→66 → 提交 `feat: freeze Identity Super Administrators client OpenAPI snapshot`

### Task 3
**Snapshot:** `openapi-client-identity-super-administrators-generate-20260822`  
薄适配 + 更新 vitest → 提交 `feat: generate Identity Super Administrators client operations`

### Task 4
**Snapshot:** `openapi-client-identity-super-administrators-verify-20260822`  
完整门禁 + 双库 slice → `Slice-passed` → `generated` → 提交 `docs: verify Identity Super Administrators OpenAPI client slice`

## 停止条件

继承 ADR-0007；失败保持 `pilot`。

## 规则与 Skill 复盘预期

无新冲突则 Verification 写一行“未新增规则/Skill 候选”。
