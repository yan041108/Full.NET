# Field Projection Authorization Implementation Plan

> For agentic workers: execute each task with a failing verification first, preserve the fresh snapshot `adminnet-absorb-07-field-projection`, and do not widen the public grant model to physical database identifiers.

**Goal:** 为 Host Users 落地服务端强制的稳定字段投影授权，并交付角色授权管理、双库迁移、OpenAPI、client-contracts、Vue/Layui 与验证证据。

**Architecture:** Identity.Contracts 定义稳定语义契约，Identity 模块维护编译期字段目录和 Dapper 解析/管理服务。角色 grant 与角色版本在同一事务更新；Host Users 列表、详情、导出共享解析器和固定 SQL 投影，不读取未授权敏感列。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper 抽象、DbUp SQL Server/MySQL、System.Text.Json source generation、Vitest/TypeScript、Vue、Layui、xUnit。

**Global Constraints:** 041 迁移成对落地；Host v1 七字段兼容；公共 API 不出现物理表列；不使用动态 SQL 标识符；不依赖客户端隐藏；不使用缓存作为授权正确性前提；所有手写代码注释使用中文。

## Task 1: 契约、目录与解析器

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity.Contracts/FieldProjectionContracts.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/FieldProjection/FieldProjectionCatalog.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/FieldProjection/UserFieldProjectionResolver.cs`
- Create: `tests/Full.NET.UnitTests/Identity/FieldProjectionResolverTests.cs`

1. 先写目录不变量、默认字段、普通角色并集、未知 grant、作用域不匹配和超级管理员边界测试并确认 RED。
2. 实现稳定键、目录校验与解析器最小行为。
3. 聚焦运行新测试并执行 Unit Release build。

## Task 2: 041 数据模型与角色字段授权 API

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/041_IdentityRoleFieldGrant.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/041_IdentityRoleFieldGrant.sql`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoleFieldGrants/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoleFieldGrants/HostRoleFieldGrantService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityDomainServiceCollectionExtensions.cs`
- Test: `tests/Full.NET.IntegrationTests/Identity/IdentityRoleFieldGrantAssertions.cs`

1. 先写双库迁移恢复、唯一约束、作用域拒绝、未知字段拒绝、版本冲突和替换原子性测试。
2. 成对实现 041，并使用角色表连接强制 Host 作用域。
3. 实现目录、读取和替换 Endpoint；授权替换与角色版本递增同事务。
4. 运行 migration/naming/global SQL catalog 与聚焦双库测试。

## Task 3: Host Users 服务端强制投影

**Files:**

- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/IdentityUserManagementContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/HostUserQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/HostUserListRow.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs`
- Test: `tests/Full.NET.UnitTests/Identity/HostUserFieldProjectionTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Identity/IdentityRoleFieldGrantAssertions.cs`

1. 先证明无 grant 时 SQL 不含三列敏感字段，有 grant 时只选择获准列，列表/详情/导出使用相同字段集合。
2. 为现有响应增加向后兼容的可选投影对象；保留七个基础字段。
3. 以有限枚举组合选择固定 SQL 投影；禁止请求值成为列标识符。
4. 新增导出 Endpoint 并要求 `identity.users.export`。

## Task 4: OpenAPI、客户端契约与双管理端

**Files:**

- Modify: `apps/Full.NET.Api/OpenApi/IdentityOpenApiExamples.cs`
- Modify: `packages/client-contracts/src/index.ts`
- Modify: `apps/admin-vue/src/api/identity.ts`
- Modify: `apps/admin-vue/src/views/identity/users/index.vue`
- Modify: `apps/admin-vue/src/views/identity/roles/index.vue`
- Modify: `apps/admin-layui/src/api/identity.ts`
- Modify: `apps/admin-layui/src/views/identity/users/index.html`
- Modify: `apps/admin-layui/src/views/identity/roles/index.html`
- Test: corresponding client-contracts, Vue and Layui tests

1. 先写契约解析与双端角色字段选择测试并确认 RED。
2. 同步目录、grant、有效字段和 Host User 投影契约。
3. 两端只按服务端有效字段显示受限列，并一致处理 403/409。
4. 运行 TypeScript、Vitest、lint 和页面 E2E 影响集。

## Task 5: 架构、affected 与交付收口

**Files:**

- Modify: `tests/Full.NET.ArchitectureTests/EndpointAuthorizationTests.cs`
- Modify: `contracts/architecture/global-sql-statements.json`
- Modify: `docs/superpowers/plans/2026-07-30-adminnet-design-absorption-program.md`
- Create: `docs/verification/field-projection-authorization-2026-08-01.md`
- Modify: `eng/testing/test-matrix.json` only after fresh discovery

1. 验证所有新 Endpoint 精确权限、SQL catalog、命名、迁移恢复和 `git diff --check`。
2. 运行 snapshot inner plan，再运行 slice affected 双库；完成 teardown 并确认 runner/Docker residual 为 0。
3. 运行 fresh Unit discovery/Architecture，与并行窗口协调后只在矩阵唯一来源更新新鲜门槛。
4. 请求独立安全代码审查，修复 Critical/Important 后冻结 Task 7 文件并释放共享资源。
