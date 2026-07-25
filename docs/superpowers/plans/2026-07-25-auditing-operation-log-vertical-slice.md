# Auditing Host 操作日志纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。在既有 `Full.NET.Modules.Auditing` 内新增垂直切片，禁止再拆 `.csproj`。

- 建立日期：2026-07-25
- 状态：**Build-verified**
- 批准依据：
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「操作与异常日志」
  - 访问日志已闭环：[`2026-07-25-auditing-access-log-vertical-slice.md`](2026-07-25-auditing-access-log-vertical-slice.md)

**Goal:** Host 分页查询已认证写操作（POST/PUT/PATCH/DELETE）的操作审计汇总行；中间件尽力写入，不记 Body/QueryString。

**Architecture:** 表 `fn_auditing_operation_log`；权限 `auditing.operations.read`；API `/api/v1/auditing/operation-logs`；导航 `operation-logs` → `/auditing/operation-logs`。

**Tech Stack:** DbUp `023` 双库迁移、Dapper、ProblemDetails、Vue/Layui、Playwright。

---

## 范围与非目标

### 必须交付

1. 双库迁移 `023_AuditingOperationLog.sql`（高写入索引策略对齐访问日志）。
2. 已认证写方法中间件尽力写入；含 `ActionKey`、`Succeeded`、可选首个权限码。
3. Host 列表 + 详情；OpenAPI + Integration 双库 + 双端只读 UI + Mock parity + 真实栈用例。

### 非目标

- 异常日志、堆栈落库、请求体落库。
- 业务 Handler 显式 `IOperationAuditor` 埋点（可后续替换/并存）。
- 标记 `Verified`。

---

## 附录 A：数据模型

| 列 | 说明 |
|---|---|
| Id | UUID v7 |
| OccurredAtUtc | 请求结束时刻 |
| ActionKey | `METHOD path`，varchar(256) |
| HttpMethod / RequestPath / StatusCode / DurationMs | 与访问日志同语义 |
| Succeeded | StatusCode &lt; 400 |
| UserId / TenantId / TraceId / ClientIpFingerprint | |
| PermissionCode | 首个 `fullnet_permission` Claim，可空 |

---

## 任务

1. [x] 迁移、权限、RED 403
2. [x] Middleware + Query + OpenAPI + Integration
3. [x] 双端 UI + E2E
4. [x] 验证记录与门槛（Integration **142 → 144**）
