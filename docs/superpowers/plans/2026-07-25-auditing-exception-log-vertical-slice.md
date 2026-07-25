# Auditing Host 异常日志纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。在既有 `Full.NET.Modules.Auditing` 内新增垂直切片。

- 建立日期：2026-07-25
- 状态：**Build-verified**
- 批准依据：[`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「操作与异常日志」；操作日志已闭环。
- 验证记录：[`auditing-exception-log-2026-07-25.md`](../../verification/auditing-exception-log-2026-07-25.md)

**Goal:** Host 分页查询未处理异常审计行；中间件捕获后重抛，不记请求体；消息与堆栈截断脱敏。

**Architecture:** 表 `fn_auditing_exception_log`；权限 `auditing.exceptions.read`；API `/api/v1/auditing/exception-logs`；导航 `exception-logs` → `/auditing/exception-logs`。Testing 环境映射探针端点以验证写入。

---

## 非目标

- 业务可预期 `Result` 失败落库、完整堆栈无限长、异常告警通道。
- 修改 `FullNetExceptionHandler` 契约（本切片用中间件捕获即可覆盖 Endpoint 异常）。
- `Verified`。

---

## 任务

1. [x] 024 迁移、权限、RED
2. [x] Middleware + Query + Testing 探针 + OpenAPI + Integration
3. [x] 双端 UI + E2E
4. [x] 文档与门槛 **144 → 146**
