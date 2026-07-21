# 租户用户-机构隶属纵向切片验证记录

- 日期：2026-07-21
- 计划：[`2026-07-21-organization-user-unit-assignment-vertical-slice.md`](../superpowers/plans/2026-07-21-organization-user-unit-assignment-vertical-slice.md)
- 状态：**Verified**（本地门禁已执行；Integration 双库依赖 Docker）

## 交付范围

| 项 | 说明 |
|---|---|
| 数据 | `fn_organization_user_unit`（迁移 `014_OrganizationUserUnit.sql`，SQL Server + MySQL） |
| API | `GET/POST /api/v1/organization/user-units`、`PUT/{id}`、`POST/{id}/disable` |
| 权限 | `organization.user_units.read` / `organization.user_units.write`（Tenant 作用域） |
| 导航 | `org-user-units` → `/organization/user-units` |
| 双管理端 | Vue `OrgUserUnitsView.vue`、Layui `org-user-units.js` |
| 跨模块 | `IHostUserDirectory` 校验活动 Host 用户 |

## 测试门槛

| 套件 | 门槛 |
|---|---|
| Integration 双库 | **95**（+2 Organization user-units SQL Server/MySQL） |
| `pnpm test:openapi` | 10/10（+2 租户用户-机构隶属夹具） |
| `pnpm test:clients` | **156**（`client-contracts` 34、Vue 58、Layui 56） |
| Mock E2E | **40**（20 场景 × 双端；+用户机构隶属 parity） |
| Real-stack E2E | **30**（15 场景 × 双端；+`host-org-user-units.spec.mjs`） |

## 本地执行摘要

| 门禁 | 结果 |
|---|---|
| `dotnet build` Organization 模块 | 通过 |
| `pnpm test:openapi` | **10/10** |
| `pnpm test:clients`（contracts 34、Vue 58、Layui 56） | **148/148**（不含 uni-app 96） |
| `pnpm test:e2e` | **40/40** |
| Integration 双库 | 未在本地 Docker 执行；门槛 **95**（+2） |
