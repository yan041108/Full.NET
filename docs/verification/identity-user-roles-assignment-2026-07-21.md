# Identity 用户-角色分配纵向切片验证记录

- 日期：2026-07-21
- 切片：`GET/PUT /api/v1/identity/users/{userId}/roles`、双端用户角色分配 UI

## 交付范围

| 层级 | 内容 |
|---|---|
| API | `HostUserRolesService`、用户角色读写端点 |
| 契约 | `HostUserRolesResponse`、`ReplaceHostUserRolesRequest`、OpenAPI 夹具 |
| 前端 | Vue `UsersView`、Layui `users.js`、i18n、`client-contracts` |
| 测试 | Integration +2（双库）、OpenAPI +2、Vue API +1、E2E 用户场景扩展 |

## 门槛（本切片后）

| 套件 | 数量 |
|---|---|
| UnitTests | **319**（不变） |
| Integration 双库 | **99**（+2 用户角色 SQL Server/MySQL） |
| `pnpm test:openapi` | **14/14**（+2 用户角色夹具） |
| `pnpm test:clients` | **158**（Vue 60、contracts 34、Layui 56、admin-i18n 8） |
| `pnpm test:e2e` | **40**（用户场景扩展角色保存） |

## 本地验证

| 命令 | 结果 |
|---|---|
| `dotnet build` | **通过** |
| UnitTests `--minimum-expected-tests 319` | **319/319** |
| `pnpm test:openapi` | **14/14** |
| `pnpm test:clients` | **158/158**（contracts 34、Vue 60、Layui 56、admin-i18n 8） |
| `pnpm test:e2e` | **40/40**（用户场景含角色保存） |
| Integration 双库 | 未在本地 Docker 执行；门槛 **99**（+2） |

## 非目标（按计划冻结）

- 运行时多角色数据范围并集
- 超级管理员角色经本 UI 管理
