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

## 2026-07-26 权限边界复核

- `PUT .../data-scope` 继续要求 Host 级 `identity.roles.write`，禁止租户令牌修改全局角色。
- `custom` 请求通过新增的可空 `tenantId` 显式指定目标租户；服务端使用该受审计参数校验机构单元，缺失时返回既有稳定机器码 `identity.data_scope.tenant_context_required`。
- SQL Server/MySQL 集成用例分别持有租户会话创建机构，并重新签发 Host 会话完成全局角色更新，锁定“租户数据校验不等于放宽 Host 管理权限”。
- 租户业务查询解析 Host 角色范围时，Identity SQL 使用 `Global` 并显式限定 `ScopeKey='host'`、`TenantId IS NULL`；参数合并器支持字典参数，按 ID 查询保持可注入的数据范围锚点。完整双库回归 **2/2**。
- Vue/Layui 当前尚未提供 Host 侧目标租户机构选择器；`custom` 的真实后端 UI 闭环仍开放，现阶段不能把 Mock 保存场景视为该闭环已验证。

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
