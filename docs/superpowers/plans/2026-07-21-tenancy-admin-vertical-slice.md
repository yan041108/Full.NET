# Tenancy 租户管理纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。本切片在既有 `fn_tenancy_tenant` 与 `ITenantProvisioningService` 之上补齐 Host 作用域租户管理 API。

- 建立日期：2026-07-21
- 状态：**Build-verified**（API + 双库 Integration + 双端 UI + Mock E2E + 真实栈脚本；真实栈本地/CI 实跑待补）
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) C2.1 租户管理
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「租户管理」
  - [`2026-07-17-fullnet-architecture-design.md`](../specs/2026-07-17-fullnet-architecture-design.md) §6.2

**Goal:** Host 管理员在宿主上下文中分页查询、创建、更新与禁用租户；复用事务 Outbox 开通流程；禁用后租户不可切换且域名解析失败。

**Architecture:** 切片落在 `Full.NET.Modules.Tenancy` 的 `Features/ManageHostTenants`；读写 `fn_tenancy_tenant`；创建走 `ITenantProvisioningService`；权限 `tenancy.tenants.read` / `tenancy.tenants.write`（Host 作用域）。

**Tech Stack:** Dapper 双库、ProblemDetails、Integration RED→GREEN；Vue/Layui 双端 UI、OpenAPI 夹具、Mock/真实栈 E2E。

---

## 范围与非目标

### 本切片必须交付

1. Host 租户分页列表、详情、创建、更新名称、禁用。
2. `tenancy.tenants.write` 权限与 Contributor 导航项 `tenant-management`。
3. 双库 Integration 夹具：权限、重复标识、乐观锁、禁用与最后一名活动租户保护。

### 明确非目标

- 租户套餐/订阅、独立数据库租户、域名 DNS 自动化。
- 修改 `Identifier` 或 `Domain`（创建后不可变）。
- 租户账号登录与租户内自助注册。

---

## 附录 A：API 契约（Task 1 冻结）

| 场景 | 方法 | 路径 | 权限 | 成功 | 失败 |
|---|---|---|---|---|---|
| 列表 | GET | `/api/v1/tenancy/tenants` | `tenancy.host_tenants.read` | 200 分页 | 403 |
| 详情 | GET | `/api/v1/tenancy/tenants/{id}` | `tenancy.host_tenants.read` | 200 | 404 |
| 创建 | POST | `/api/v1/tenancy/tenants` | write | 201 | 409 `tenancy.identifier_exists` / `tenancy.domain_exists` |
| 更新 | PUT | `/api/v1/tenancy/tenants/{id}` | write | 200 | 409 `tenancy.tenant.version_conflict` |
| 禁用 | POST | `/api/v1/tenancy/tenants/{id}/disable` | write | 200 | 422 `tenancy.tenant.last_remaining` |

响应体复用 `TenantSummary`；创建请求复用 `ProvisionTenantRequest`；更新请求 `UpdateHostTenantRequest(Name, Version)`。

---

## Task 进度

### Task 1: 规格冻结与 RED 夹具

- [x] 本计划附录 A。
- [x] `TenancyHostTenantManagementAssertions` 双库 RED→GREEN。
- [x] Integration 门槛 **126 → 128**（+2 SQL Server/MySQL）。

### Task 2–4（后续）

- [x] API、权限、导航 Contributor
- [x] Vue/Layui 双端 UI
- [x] OpenAPI 夹具、Mock 双端 E2E
- [x] 真实栈 E2E 脚本（`host-tenants.spec.mjs`）；本地/CI 实跑待补
- [x] 验证记录 [`tenancy-host-tenant-management-2026-07-23.md`](../../verification/tenancy-host-tenant-management-2026-07-23.md)
