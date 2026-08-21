# OpenAPI 客户端迁移：Identity Host Online Sessions（Identity remaining 第 4 slice）

> **For agentic workers:** 按本计划逐步执行；每 Task 独立 snapshot；行为变更必须 RED→GREEN。勾选或提交存在不能替代新鲜 Verification。

**Goal:** 将 `ui/admin/src/api/online-sessions.ts` 收缩为 OpenAPI 生成 Operation 薄适配层（`identity-host-online-sessions`）。

**Architecture:** 延续 ADR-0007 与 Roles / Menus / API Keys slice 模式。

**Approved basis:** ADR-0007；API Keys [`Slice-passed`](../../verification/openapi-client-identity-host-api-keys-2026-08-22.md)。

**Baseline:** `3e37a9d50dc7a8120d7959a4334d76c56a7094ae`。每 Task 开始时重记 `git rev-parse HEAD`。

## 执行状态（2026-08-22）

- 计划已创建；实现进行中。

## Global Constraints

- 唯一 `generatedGroup`：`identity-host-online-sessions`。
- `ui/admin-layui/**` 零修改；禁止改路径/方法/状态码/序列化形状。
- 只允许补齐 `WithName`、主 Tag、`.Produces` / ProblemDetails。
- JSON：`unknown → generated guard → DTO`；保留页面签名与查询 trim 语义。
- 禁止并行其他资源组。

## 目标 Operation

主 Tag：`IdentityHostOnlineSessions`。

| Method | Path | operationId | Vue 导出 |
| --- | --- | --- | --- |
| GET | `/api/v1/identity/online-sessions` | `identityListHostOnlineSessions` | `listHostOnlineSessions` |
| POST | `/api/v1/identity/online-sessions/{sessionId}/revoke` | `identityRevokeHostOnlineSession` | `revokeHostOnlineSession` |

---

### Task 1
**Snapshot:** `openapi-client-identity-host-online-sessions-metadata-20260822`  
Endpoint + Architecture + OpenAPI 夹具 → inner → 提交 `feat: stabilize Identity Host Online Sessions OpenAPI identities`

### Task 2
**Snapshot:** `openapi-client-identity-host-online-sessions-snapshot-20260822`  
2 条 `pilot`；54→56；snapshot `--update` + generate → 提交 `feat: freeze Identity Host Online Sessions client OpenAPI snapshot`

### Task 3
**Snapshot:** `openapi-client-identity-host-online-sessions-generate-20260822`  
薄适配 `online-sessions.ts` → 提交 `feat: generate Identity Host Online Sessions client operations`

### Task 4
**Snapshot:** `openapi-client-identity-host-online-sessions-verify-20260822`  
完整门禁 + 双库 slice（`--base` Task1 前）→ Verification `Slice-passed` → `generated` → 提交 `docs: verify Identity Host Online Sessions OpenAPI client slice`

## 停止条件

继承 ADR-0007；失败保持 `pilot`，禁止开始下一资源组。

## 规则与 Skill 复盘预期

无新冲突则 Verification 写一行“未新增规则/Skill 候选”。
