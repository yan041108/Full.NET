# Auditing Host 访问日志纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。新建 `Full.NET.Modules.Auditing` 主项目；Contracts 放主项目内 `Contracts/`，禁止再拆 `.Contracts` / `.Http`。

- 建立日期：2026-07-25
- 状态：**Build-verified（Task 1–4）**
- 批准依据：
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「访问日志」
  - [`capability-status.md`](../../roadmap/capability-status.md) / 客户端路线图 C2.2
  - [总体架构 §6.5](../specs/2026-07-17-fullnet-architecture-design.md#65-auditing)

**Goal:** Host 管理员分页查询 HTTP 访问审计汇总行；中间件在请求结束后尽力写入，不记 QueryString/Body。

**Architecture:** 新模块 `Auditing`；表 `fn_auditing_access_log`；权限 `auditing.access.read`；API `/api/v1/auditing/access-logs`；导航 `access-logs` → `/auditing/access-logs`。`fn_identity_auth_audit` 仍属 Identity。

**Tech Stack:** DbUp `022` 双库迁移、Dapper、ProblemDetails、Vue/Layui、Playwright。

---

## 范围与非目标

### 必须交付

1. 双库迁移 `022_AuditingAccessLog.sql`（高写入：NONCLUSTERED PK + 时间聚集索引）。
2. `BeforeEndpoints` 中间件尽力写入；排除 `/health/*`、`/openapi`、`/scalar`。
3. Host 分页列表 + 按 Id 详情；OpenAPI + Integration 双库 + 双端只读 UI + Mock parity + 真实栈冒烟。

### 非目标

- 操作日志、异常日志、数据变更审计、慢 SQL。
- 请求体/响应体/QueryString 落库。
- 保留清理任务、可靠 Outbox 审计通道。
- 暴露 Identity `fn_identity_auth_audit`。
- 标记 `Verified`。

---

## 附录 A：数据模型

### `fn_auditing_access_log`

| 列 | 说明 |
|---|---|
| Id | UUID v7 |
| OccurredAtUtc | 请求结束时刻 |
| HttpMethod | varchar(16) |
| RequestPath | nvarchar(512)，无 query |
| StatusCode | int |
| DurationMs | int |
| UserId / TenantId | Guid? |
| TraceId | varchar(64)? |
| ClientIpFingerprint | varchar(64)? |
| IsAuthenticated | bit |

---

## 附录 B：API

| 场景 | 方法 | 路径 | 权限 |
|---|---|---|---|
| 列表 | GET | `/api/v1/auditing/access-logs` | `auditing.access.read` |
| 详情 | GET | `/api/v1/auditing/access-logs/{id}` | 同上 |

---

## 任务分解

### Task 1: 模块骨架、迁移与 RED

1. [x] 本计划。
2. [x] `Full.NET.Modules.Auditing` + Composition 注册。
3. [x] `022` 双库迁移；权限/导航。
4. [x] RED：列表 403；Integration **140 → 142**。

### Task 2: 中间件 + 查询 API

1. [x] Writer + Middleware + Query + Endpoint + OpenAPI。
2. [x] Integration 完整断言（写入可查）。

### Task 3: 双端 UI 与 E2E

1. [x] contracts / i18n / Vue / Layui。
2. [x] shell-parity + 真实栈；门槛上调。

### Task 4: 验证记录与状态矩阵

1. [x] `docs/verification/auditing-access-log-2026-07-25.md`
2. [x] capability / adminnet-feature-parity / threshold audit
