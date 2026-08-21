# OpenAPI 客户端迁移：Identity TOTP Enrollment（Identity remaining 第 7 slice）

> **For agentic workers:** 按本计划逐步执行；每 Task 独立 snapshot；行为变更必须 RED→GREEN。

**Goal:** 将 `ui/admin/src/api/totpEnrollment.ts` 收缩为 OpenAPI 生成 Operation 薄适配层（`identity-totp-enrollment`）。

**Architecture:** 延续 ADR-0007 与既有 Identity remaining slice 模式。仅覆盖当前用户自助 TOTP 登记三端点。

**Approved basis:** ADR-0007；Me [`Slice-passed`](../../verification/openapi-client-identity-me-2026-08-22.md)。

**Baseline:** `ab9e6fcb3b04403f47a5944203e494b752f69307`。

## 执行状态（2026-08-22）

- 计划已创建；实现进行中。

## Global Constraints

- 唯一 `generatedGroup`：`identity-totp-enrollment`。
- 仅覆盖 `/api/v1/identity/me/mfa/totp` 三端点；不纳入 Super Administrators 或 auth/session。
- `ui/admin-layui/**` 零修改；禁止改路径/方法/状态码/序列化形状。
- 只允许补齐 `WithName`、主 Tag、`Produces` / ProblemDetails。
- 既有 `totpEnrollment.test.ts` 必须改为 mock `http` 并随薄适配保持 GREEN。
- 禁止并行其他资源组。

## 目标 Operation

主 Tag：`IdentityTotpEnrollment`。

| Method | Path | operationId | Vue 导出 |
| --- | --- | --- | --- |
| GET | `/api/v1/identity/me/mfa/totp` | `identityGetTotpEnrollmentStatus` | `getTotpEnrollmentStatus` |
| POST | `/api/v1/identity/me/mfa/totp/begin` | `identityBeginTotpEnrollment` | `beginTotpEnrollment` |
| POST | `/api/v1/identity/me/mfa/totp/confirm` | `identityConfirmTotpEnrollment` | `confirmTotpEnrollment` |

---

### Task 1
**Snapshot:** `openapi-client-identity-totp-enrollment-metadata-20260822`  
→ 提交 `feat: stabilize Identity TOTP Enrollment OpenAPI identities`

### Task 2
**Snapshot:** `openapi-client-identity-totp-enrollment-snapshot-20260822`  
3 条 `pilot`；59→62 → 提交 `feat: freeze Identity TOTP Enrollment client OpenAPI snapshot`

### Task 3
**Snapshot:** `openapi-client-identity-totp-enrollment-generate-20260822`  
薄适配 + 更新 vitest → 提交 `feat: generate Identity TOTP Enrollment client operations`

### Task 4
**Snapshot:** `openapi-client-identity-totp-enrollment-verify-20260822`  
完整门禁 + 双库 slice → `Slice-passed` → `generated` → 提交 `docs: verify Identity TOTP Enrollment OpenAPI client slice`

## 停止条件

继承 ADR-0007；失败保持 `pilot`。

## 规则与 Skill 复盘预期

无新冲突则 Verification 写一行“未新增规则/Skill 候选”。
