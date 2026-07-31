# Identity Host 用户管理真实栈 E2E 纵向切片

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。API 与双端 UI 已 Build-verified；本切片只补真实后端浏览器写路径，不扩展后端契约。

- 建立日期：2026-07-29
- 状态：Build-verified
- 目标：将 `host-users.spec.mjs` 从「列表 + 403」扩展到与 `shell-parity` 对等的创建、更新、禁用、启用真实栈路径，推进 C2.1 用户管理向 `Verified` 靠拢。

## 范围

1. Vue + Layui 双端：经 UI 创建 Host 用户、更新显示名称、禁用、启用。
2. 唯一用户名/显示名（含 clientKind 后缀），避免持久化栈重复数据导致严格模式失败。
3. 可选：禁用后登录 API 返回 `identity.invalid_credentials`。

## 非目标

- 角色分配、重置密码真实栈（已有独立 API 验证与 Mock parity）。
- 租户级用户、新后端端点。

## 退出条件

1. `pnpm --filter @fullnet/admin-real-stack-e2e exec playwright test tests/host-users.spec.mjs` 全绿（SQL Server 真实栈）。
2. 更新 `docs/verification/identity-user-management-2026-07-21.md` 真实栈章节与 `capability-status` 证据列（若本任务合入）。
