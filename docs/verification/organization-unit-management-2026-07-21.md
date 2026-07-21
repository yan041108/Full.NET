# Organization 机构管理纵向切片验证记录

- 日期：2026-07-21
- 状态：**Build-verified**（双库集成与真实栈 E2E 依赖 CI）
- 计划：[`2026-07-21-organization-unit-management-vertical-slice.md`](../superpowers/plans/2026-07-21-organization-unit-management-vertical-slice.md)

## 交付范围

| 项 | 说明 |
|---|---|
| 模块 | `Full.NET.Modules.Organization` + Contracts |
| 迁移 | `013_OrganizationUnit.sql`（SQL Server / MySQL） |
| API | `GET/POST /api/v1/organization/units`、`GET/PUT /{id}`、`POST /{id}/disable` |
| 权限 | `organization.units.read` / `organization.units.write`（Tenant 作用域） |
| 导航 | Contributor `org-units` → `/organization/units` |
| 白名单 | `AdminNavigationWhitelist` 与 `navigation-catalog.ts` 同步 |
| 双端 UI | Vue `OrgUnitsView.vue`、Layui `org-units.js` |
| OpenAPI 夹具 | `contracts/openapi/organization-tenant-units-v1.json`、`pnpm test:openapi`、Integration OpenAPI 断言 |
| Mock parity E2E | `shell-parity.spec.mjs` 机构列表/创建/禁用（**38** 项总门槛中的 1 场景 × 双端） |
| 真实栈冒烟 | `host-org-units.spec.mjs`：租户上下文中管理员列表 + 受限账号 API/UI 403（**28** 项总门槛中的 2 场景 × 双端） |
| 客户端契约 | `packages/client-contracts/src/tenant-org-units.ts` |

## 本地验证（2026-07-21）

| 命令 | 结果 |
|---|---|
| `dotnet build` | 通过 |
| `dotnet test` UnitTests | 通过（315） |
| `dotnet test` ArchitectureTests | 通过（27） |
| `pnpm test:openapi` | 通过（8/8，含租户机构 2 项） |
| `pnpm test:clients` | 通过（152 项管理端相关 + uni-app 96） |
| Integration 双库 | 未在本地 Docker 执行；门槛 **93**（+2 Organization SQL Server/MySQL） |

## 真实栈验收要点（CI）

1. Host 超级管理员进入 `Full.NET Local` 租户后可见「机构管理」，空列表文案正确。
2. `e2e-viewer` 在租户上下文中调用机构 API 返回 `authorization.permission_denied`，导航无机构管理，直链 `#/organization/units` 展示 403。

## 非目标（留后续切片）

- 职位/职级、用户-组织关系、数据范围规则与 SQL 投影。
