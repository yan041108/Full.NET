# Organization 可分配 Host 用户目录实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在租户上下文中为用户—机构、用户—职位隶属表单提供可分页选择的活动 Host 用户目录，并保持既有 Host 用户管理权限和租户隔离边界不变。

**Architecture:** Identity 通过新的只读跨模块目录接口，以 `SqlDataScope.Global` 执行显式限定 `ScopeKey='host'`、`TenantId IS NULL`、`IsActive=1` 的双库分页 SQL。Organization 暴露两个功能内聚路由，共享一个查询服务，但分别使用 `organization.user_units.write` 与 `organization.user_positions.write` 精确授权；Vue/Layui 不再直接请求 Host 用户管理 API。

**Tech Stack:** .NET 10、Minimal API、Dapper SQL、System.Text.Json 源生成、Vue 3/TypeScript、Layui、Microsoft Testing Platform、Vitest、Playwright。

## Global Constraints

- 不新增数据库对象或迁移；只新增活动 Host 用户分页查询。
- Host 用户目录 SQL 必须显式限定 Host 行，且 SQL Server/MySQL 使用稳定用户名排序。
- 候选端点必须要求租户上下文，并分别使用对应隶属写权限；不得放宽 `identity.users.read`。
- 公共 API 使用标准状态码、camelCase JSON、OpenAPI 冻结夹具和最小字段投影。
- Vue 与 Layui 必须同步接入；只读账号不得因候选端点 403 阻断隶属列表。

---

### Task 1: Identity 跨模块候选目录

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/IHostUserDirectory.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/HostUsers/HostUserSelectionDirectory.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityDomainServiceCollectionExtensions.cs`
- Test: `tests/Full.NET.UnitTests/Identity/HostUserDirectoryTests.cs`
- Test: `tests/Full.NET.UnitTests/Identity/IdentityModuleRegistrationTests.cs`
- Test: `tests/Full.NET.UnitTests/Data/HostCatalogSqlScopeTests.cs`

**Interfaces:**
- Produces: `IHostUserSelectionDirectory.ListActiveHostUsersAsync(int page, int pageSize, CancellationToken)`。
- Produces: `PagedResult<HostUserDirectoryEntry>`，仅包含 `Id`、`Username`、`DisplayName`。

- [x] **Step 1: 写入失败测试**

扩展现有 Identity Unit 测试，断言 SQL Server/MySQL 选择正确 Statement、分页参数被规范化、Statement 为 `Global` 且 SQL 包含 Host/活动用户过滤。

- [x] **Step 2: 运行 RED**

Run: `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~HostUserDirectoryTests|FullyQualifiedName~HostCatalogSqlScopeTests|FullyQualifiedName~IdentityModuleRegistrationTests"`

Expected: 因接口、实现和 Statement 尚不存在而失败。

- [x] **Step 3: 实现最小目录**

新增接口：

```csharp
public interface IHostUserSelectionDirectory
{
    Task<PagedResult<HostUserDirectoryEntry>> ListActiveHostUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
```

实现中将 `page` 规范为至少 `1`，`pageSize` 限制在 `1..100`，根据 `DatabaseOptions.Provider` 选择双库分页 SQL，并注册 Scoped 服务。

- [x] **Step 4: 运行 GREEN**

Run: 与 Step 2 相同。

Expected: 相关测试全部通过。

### Task 2: Organization 精确授权候选 API

**Files:**
- Create: `src/Modules/Full.NET.Modules.Organization.Contracts/OrganizationAssignableUserContracts.cs`
- Create: `src/Modules/Full.NET.Modules.Organization/Features/ListAssignableHostUsers/AssignableHostUserQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserUnits/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserPositions/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/OrganizationModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Serialization/OrganizationJsonSerializerContext.cs`
- Modify: `contracts/openapi/organization-tenant-user-units-v1.json`
- Modify: `contracts/openapi/organization-tenant-user-positions-v1.json`
- Test: `tests/openapi/organization-tenant-user-units-contract.test.mjs`
- Test: `tests/openapi/organization-tenant-user-positions-contract.test.mjs`
- Test: `tests/Full.NET.IntegrationTests/Organization/OrganizationUserUnitManagementAssertions.cs`
- Test: `tests/Full.NET.IntegrationTests/Organization/OrganizationUserPositionManagementAssertions.cs`

**Interfaces:**
- Consumes: `IHostUserSelectionDirectory`。
- Produces: `GET /api/v1/organization/user-units/assignable-users`，权限 `organization.user_units.write`。
- Produces: `GET /api/v1/organization/user-positions/assignable-users`，权限 `organization.user_positions.write`。
- Produces: `PagedResult<OrganizationAssignableUserResponse>`。

- [x] **Step 1: 写入失败契约**

先扩展两份 OpenAPI Node 契约和双库 Integration 断言，要求候选端点返回活动管理员、分页字段完整，并拒绝缺少对应写权限的租户身份。

- [x] **Step 2: 运行 RED**

Run: `node --test tests/openapi/organization-tenant-user-units-contract.test.mjs tests/openapi/organization-tenant-user-positions-contract.test.mjs`

Expected: 因候选路由、响应契约和 Endpoint 标记不存在而失败。

- [x] **Step 3: 实现最小 API**

新增响应：

```csharp
public sealed record OrganizationAssignableUserResponse(
    Guid Id,
    string Username,
    string DisplayName);
```

共享查询服务先验证 `ICurrentTenant` 为有效租户，再调用 Identity 目录并映射最小投影。两个 Endpoint 只共享服务，不共享权限策略。

- [x] **Step 4: 运行契约 GREEN 与双库影响集**

Run: Step 2 命令，然后运行任务快照的 `inner` affected 计划与执行命令。

Expected: OpenAPI 契约通过；SQL Server/MySQL Organization 影响集通过。

### Task 3: 双管理端接入与真实栈

**Files:**
- Create: `packages/client-contracts/src/tenant-org-assignable-users.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Modify: `ui/admin/src/api/org-user-units.ts`
- Modify: `ui/admin/src/api/org-user-positions.ts`
- Modify: `ui/admin/src/views/OrgUserUnitsView.vue`
- Modify: `ui/admin/src/views/OrgUserPositionsView.vue`
- Modify: `ui/admin-layui/js/core/org-user-units.js`
- Modify: `ui/admin-layui/js/core/org-user-positions.js`
- Test: `packages/client-contracts/tests/tenant-org-assignable-users.test.ts`
- Test: `ui/admin/src/api/org-user-units.test.ts`
- Test: `ui/admin/src/api/org-user-positions.test.ts`
- Test: `ui/admin-layui/tests/org-user-units.test.js`
- Test: `ui/admin-layui/tests/org-user-positions.test.js`
- Test: `tests/e2e/admin-real-stack/tests/host-org-user-positions.spec.mjs`

**Interfaces:**
- Consumes: 两个 `assignable-users` 分页 API。
- Produces: `OrganizationAssignableUser` 与运行时守卫。
- Produces: 双端相同候选加载和只读账号降级行为。

- [x] **Step 1: 写入客户端 RED**

客户端 API 测试要求调用新路由并拒绝畸形响应；Layui 控制器测试要求不再调用 `/api/v1/identity/users` 或 `/api/v1/me`。

- [x] **Step 2: 运行 RED**

Run: `pnpm --filter @fullnet/client-contracts test -- tenant-org-assignable-users`

Run: `pnpm --filter @fullnet/admin test -- org-user-units org-user-positions`

Run: `pnpm --filter @fullnet/admin-layui test -- tests/org-user-units.test.js tests/org-user-positions.test.js`

Expected: 因共享契约和客户端方法尚不存在而失败。

- [x] **Step 3: 实现双端最小接入**

Vue 在 `canWrite` 时请求对应候选 API，否则使用空数组；Layui 请求候选 API并在 403 时降级为空。删除租户页面对 `/api/v1/identity/users` 与 `/api/v1/me` 的依赖。

- [x] **Step 4: 运行客户端 GREEN 与真实栈**

运行 Step 2 命令、Vue 类型检查，以及用户职位真实栈 SQL Server/MySQL 聚焦场景。

Expected: 双端单测、类型检查和双库真实栈通过。

- [x] **Step 5: 收口**

更新既有能力状态与验证记录，运行 `pnpm test:integration:affected -- --snapshot organization-assignable-host-users-20260729 --phase slice`、`git diff --check` 与任务范围 `git status`；不新增规则或 Skill。
