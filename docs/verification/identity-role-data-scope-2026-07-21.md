# Identity 角色数据范围纵向切片验证记录

- 日期：2026-07-21
- 切片：`GET/PUT /api/v1/identity/roles/{id}/data-scope`、`RoleDataScopeProjection`、双端角色数据范围 UI

## 交付范围

| 层级 | 内容 |
|---|---|
| 迁移 | `015_HostRoleDataScope.sql`（SQL Server + MySQL） |
| API | `HostRoleDataScopeService`、数据范围读写端点 |
| 跨模块 | `ITenantOrganizationUnitDirectory` 校验自定义机构单元 |
| 投影 | `RoleDataScopeProjection` 参数化 SQL 片段 |
| 前端 | Vue `RolesView`、Layui `roles.js`、i18n、`client-contracts` |
| 测试 | 单元 4 项、Integration +2（双库）、OpenAPI +2、客户端/E2E 扩展 |

## 门槛（本切片后）

| 套件 | 数量 |
|---|---|
| UnitTests | **319**（+4 投影） |
| Integration 双库 | **97**（+2 数据范围 SQL Server/MySQL） |
| `pnpm test:openapi` | **12/12**（+2 数据范围夹具） |
| `pnpm test:clients` | **157**（contracts 34、Vue 59、Layui 56、admin-i18n 8） |
| `pnpm test:e2e` | **40**（角色场景扩展数据范围保存） |

## 本地验证

| 命令 | 结果 |
|---|---|
| `dotnet build`（Identity/Organization/Tests） | **通过** |
| UnitTests `--minimum-expected-tests 319` | **319/319** |
| `pnpm test:openapi` | **12/12** |
| `pnpm test:clients` | **157/157**（contracts 34、Vue 59、Layui 56、admin-i18n 8） |
| `pnpm test:e2e` | **40/40**（角色场景含数据范围保存） |
| Integration 双库 | 未在本地 Docker 执行；门槛 **97**（+2） |

## 非目标（按计划冻结）

- 用户-角色分配 UI
- 运行时多角色数据范围并集
- 业务模块全面接入机构过滤
