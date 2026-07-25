# Tenancy 租户套餐纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。本切片在 Host 租户管理之上交付套餐目录 CRUD，暂不绑定租户订阅。

- 建立日期：2026-07-23
- 状态：**Build-verified**（API + 双库 Integration + 双端 UI + Mock E2E + 真实栈脚本；真实栈本地/CI 实跑待补）
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) C2.1 套餐
  - [`client-delivery-roadmap.md`](../../roadmap/client-delivery-roadmap.md) C2.1 下一优先项
  - [`2026-07-17-fullnet-architecture-design.md`](../specs/2026-07-17-fullnet-architecture-design.md) §6.3

**Goal:** Host 管理员维护租户套餐目录（编码、名称、描述、启停）；为后续租户分配套餐与限制策略提供稳定主数据。

**Architecture:** 切片落在 `Full.NET.Modules.Tenancy` 的 `Features/ManageHostTenantPackages`；读写 `fn_tenancy_tenant_package`；权限 `tenancy.tenant_packages.read` / `tenancy.tenant_packages.write`（Host 作用域）。

**Tech Stack:** Dapper 双库、ProblemDetails、Integration RED→GREEN；双端 UI、OpenAPI、E2E 在 Task 3–5 交付。

---

## 范围与非目标

### 本切片必须交付

1. 迁移 `018_TenancyTenantPackage.sql`（SQL Server + MySQL）。
2. Host 套餐分页列表、详情、创建、更新名称/描述、禁用。
3. 权限与导航项 `tenant-packages`。
4. 双库 Integration：权限、重复编码、乐观锁、禁用。

### 明确非目标

- 租户与套餐绑定、过期策略、配额限制与计费。
- 套餐功能开关矩阵（Settings 模块后续承接）。
- 独立数据库租户连接工厂。

---

## 附录 A：API 契约（Task 1 冻结）

| 场景 | 方法 | 路径 | 权限 | 成功 | 失败 |
|---|---|---|---|---|---|
| 列表 | GET | `/api/v1/tenancy/tenant-packages` | `tenancy.tenant_packages.read` | 200 分页 | 403 |
| 详情 | GET | `/api/v1/tenancy/tenant-packages/{id}` | read | 200 | 404 |
| 创建 | POST | `/api/v1/tenancy/tenant-packages` | write | 201 | 409 `tenancy.tenant_package.code_exists` |
| 更新 | PUT | `/api/v1/tenancy/tenant-packages/{id}` | write | 200 | 409 `tenancy.tenant_package.version_conflict` |
| 禁用 | POST | `/api/v1/tenancy/tenant-packages/{id}/disable` | write | 200 | 404 |

响应体 `TenantPackageSummary`：`id`、`code`、`name`、`description`、`isActive`、`version`。

---

## Task 进度

### Task 1: 规格冻结与 RED 夹具

- [x] 本计划附录 A。
- [x] `TenancyHostTenantPackageManagementAssertions` 双库 RED→GREEN。
- [x] Integration 门槛 **128 → 130**（+2 SQL Server/MySQL）。

### Task 2（已完成）

- [x] 迁移、API、权限、导航 Contributor
- [x] Vue/Layui 双端 UI、client-contracts、i18n
- [x] OpenAPI 夹具、Mock 双端 E2E
- [x] 真实栈 E2E 脚本（`host-tenant-packages.spec.mjs`）

### Task 3–5（后续）

- [ ] 验证记录与 capability 同步
- [ ] 真实栈本地/CI 全量实跑
- [ ] 租户-套餐绑定
- [ ] 验证记录与 capability 同步
