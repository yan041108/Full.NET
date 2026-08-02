# Vue Page and Action Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立失败关闭的“模块/页面/操作”授权目录，以 Identity Users 为第一个完整切片，实现角色逐按钮授权、Vue 无权限不渲染和直接 API 403，并停止新增 Layui 交付。

**Architecture:** 扩展现有代码拥有的 Authorization Catalog，页面继续由 `NavigationDefinition.RequiredPermission` 控制，业务操作由新的 `AuthorizationActionDefinition` 关联页面和稳定权限码。角色仍把精确权限码存入 `fn_identity_role_permission`，Vue 消费结构化授权树并通过统一响应式权限门控制 DOM；Endpoint 使用同一权限码，架构测试拒绝未知或缺失授权。存量 `identity.users.write` 通过成对可恢复迁移展开为等价动作权限。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper、DbUp、SQL Server、MySQL、System.Text.Json Source Generation、Vue 3、TypeScript、Element Plus、Pinia、Vitest、MSTest、Playwright。

## Global Constraints

- 设计权威：[Vue 单一后台与页面/操作精确授权设计](../specs/2026-08-02-vue-action-authorization-design.md)。
- 新后台功能只写 `ui/admin`；`ui/admin-layui/**`、Layui 测试、Layui E2E 和 Layui 生成模板必须零新增、零修改。
- 权限码是稳定业务标识，禁止使用 URL、HTTP Method、组件路径或显示文本。
- 无权限 Vue 元素不进入 DOM；客户端隐藏不能替代服务端 Endpoint 授权。
- 未知权限、孤立操作、缺少页面父权限和未声明授权意图必须失败关闭。
- 超级管理员通过动态目录获得已知权限，但仍执行账号、会话、作用域、Endpoint、审计和最后一名保护。
- 数据迁移必须同时提供 SQL Server/MySQL、半完成恢复和真实双库 Integration。
- 实施前创建 fresh snapshot：`pnpm test:task:start -- admin-action-permissions-identity-users-20260802`；后续所有 affected 命令使用该快照。
- `054_IdentityUserActionPermissions` 只是截至 2026-08-02 的候选号。实施前必须确认 SQL Server/MySQL 均未占用；任一已占用时停止并重新协调，禁止改写或重命名已发布迁移。

---

## File Map

| 文件 | 责任 |
| --- | --- |
| `src/Modules/Full.NET.Modules.Identity.Contracts/AuthorizationActionDefinition.cs` | 定义稳定页面操作目录项 |
| `src/Modules/Full.NET.Modules.Identity.Contracts/IAuthorizationCatalogContributor.cs` | 允许模块贡献操作目录 |
| `src/Modules/Full.NET.Modules.Identity/Authorization/AuthorizationCatalog.cs` | 聚合并验证权限、导航和操作关系 |
| `src/Modules/Full.NET.Modules.Identity/Features/GetAuthorizationTree/*` | 输出角色授权页使用的结构化目录 |
| `src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/HostRoleManagementService.cs` | 复验页面/操作父子授权不变量 |
| `src/Modules/Full.NET.Modules.Identity.Contracts/IdentityUserManagementContracts.cs` | 冻结 Users 精确动作权限码 |
| `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/Endpoint.cs` | 将每个 Users Endpoint 绑定精确权限 |
| `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/*/054_IdentityUserActionPermissions.sql` | 展开存量角色和 API Key 的 Users 写权限 |
| `packages/client-contracts/src/authorization-tree.ts` | 校验不可信授权树 JSON |
| `ui/admin/src/auth/permission.ts`、`ui/admin/src/components/PermissionGate.vue` | Vue 响应式失败关闭权限门 |
| `ui/admin/src/views/RolesView.vue` | 模块/页面/操作树形授权 |
| `ui/admin/src/views/UsersView.vue` | 每个业务按钮使用独立权限 |
| `tests/Full.NET.ArchitectureTests/EndpointAuthorizationTests.cs` | 拒绝未知权限和缺失显式权限的 Endpoint |

### Task 1: 扩展 Authorization Catalog 操作模型

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity.Contracts/AuthorizationActionDefinition.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/IAuthorizationCatalogContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Authorization/AuthorizationCatalog.cs`
- Test: `tests/Full.NET.UnitTests/Identity/AuthorizationCatalogTests.cs`

**Interfaces:**
- Produces: `AuthorizationActionDefinition(string Id, string NavigationId, string PermissionCode, string Name, string ClientActionKey, int Order)`
- Produces: `IAuthorizationCatalogContributor.Actions` with an empty default implementation so unmigrated modules remain source-compatible
- Preserves: existing `Permissions` and `Navigation` ordering and validation

- [ ] **Step 1: Write failing catalog tests**

Add tests that create contributors with an unknown navigation, unknown permission, duplicate action ID, duplicate `(NavigationId, ClientActionKey)` and valid ordered actions. The valid assertion must prove ordinal ordering by navigation order, action order and ID.

```csharp
[TestMethod]
public void Create_rejects_action_with_unknown_navigation()
{
    var contributor = new StubContributor(
        [new PermissionDefinition("identity.users.read", "查看用户", AuthorizationScope.Host)],
        [],
        [new AuthorizationActionDefinition(
            "identity.users.create",
            "missing-users-page",
            "identity.users.create",
            "创建用户",
            "create",
            10)]);

    Assert.ThrowsExactly<InvalidOperationException>(
        () => AuthorizationCatalog.Create([contributor]));
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~AuthorizationCatalogTests
```

Expected: compilation/test failure because `AuthorizationActionDefinition` and `Actions` do not exist.

- [ ] **Step 3: Implement the immutable action definition and validation**

Create the record exactly as frozen in the design. Add `Actions => []` as a default interface member, then extend the catalog constructor and `Create` method with `Actions`, validate all required strings, known navigation/permission references and uniqueness, and expose a deterministically sorted `IReadOnlyList<AuthorizationActionDefinition>`.

- [ ] **Step 4: Run GREEN and commit**

Run the Task 1 test command again. Expected: all discovered `AuthorizationCatalogTests` pass.

```powershell
git add src/Modules/Full.NET.Modules.Identity.Contracts src/Modules tests/Full.NET.UnitTests/Identity/AuthorizationCatalogTests.cs
git commit -m "feat(identity): add page action authorization catalog"
```

### Task 2: 冻结 Users 精确权限并加强 Endpoint 架构门禁

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/IdentityUserManagementContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/Endpoint.cs`
- Modify: `tests/Full.NET.ArchitectureTests/EndpointAuthorizationTests.cs`
- Test: `tests/Full.NET.UnitTests/Identity/AuthorizationCatalogTests.cs`

**Interfaces:**
- Produces constants: `Read`, `Create`, `Update`, `AssignRoles`, `ResetPassword`, `Disable`, `Enable`, `Export`
- Removes from assignable catalog after migration: `identity.users.write`
- Endpoint mapping: list/detail/role-read use `Read`; create/update/role-replace/reset/disable/enable/export use their exact constants

- [ ] **Step 1: Write RED architecture and catalog assertions**

Assert the exact permission set and action bindings:

```csharp
var expected = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["create"] = "identity.users.create",
    ["update"] = "identity.users.update",
    ["assign-roles"] = "identity.users.assign_roles",
    ["reset-password"] = "identity.users.reset_password",
    ["disable"] = "identity.users.disable",
    ["enable"] = "identity.users.enable",
    ["export"] = "identity.users.export",
};
```

Extend `EndpointAuthorizationTests` so a test-only Endpoint using `FullNET.Permission:unknown.permission` is reported as invalid, in addition to the existing missing authorization check.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter FullyQualifiedName~EndpointAuthorizationTests
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~AuthorizationCatalogTests
```

Expected: exact permission/action assertions fail while Users still exposes `Write`.

- [ ] **Step 3: Implement exact permissions and Endpoint bindings**

Register seven Users actions under navigation ID `users`. Do not register `Read` as a button action; it remains the page permission. Replace every Users write Endpoint policy with the frozen exact constant.

- [ ] **Step 4: Run GREEN and commit**

Run both Task 2 commands. Expected: all selected tests pass with non-zero discovery.

```powershell
git add src/Modules/Full.NET.Modules.Identity.Contracts/IdentityUserManagementContracts.cs src/Modules/Full.NET.Modules.Identity tests/Full.NET.ArchitectureTests/EndpointAuthorizationTests.cs tests/Full.NET.UnitTests/Identity/AuthorizationCatalogTests.cs
git commit -m "feat(identity): split host user action permissions"
```

### Task 3: 提供结构化授权树 API

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity.Contracts/AuthorizationTreeContracts.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/GetAuthorizationTree/AuthorizationTreeProjector.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/GetAuthorizationTree/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs`
- Test: `tests/Full.NET.UnitTests/Identity/AuthorizationTreeProjectorTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Identity/IdentityRoleManagementAssertions.cs`
- Create: `contracts/openapi/identity-authorization-tree-v1.json`
- Create: `tests/Full.NET.IntegrationTests/Api/OpenApiAuthorizationTreeContractAssertions.cs`
- Create: `tests/openapi/identity-authorization-tree-contract.test.mjs`

**Interfaces:**
- Produces: `GET /api/v1/identity/authorization-tree`
- Requires: `identity.roles.read`
- Produces: `AuthorizationTreePageResponse` with nested `Children` and ordered `Actions`

- [ ] **Step 1: Write RED projector/API tests**

Cover deterministic ordering, page/action permission fields, exclusion of non-assignable super-administrator permissions, no component path/HTML fields, Host scope filtering and direct `403` without `identity.roles.read`.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~AuthorizationTreeProjectorTests
```

Expected: compilation failure because the projector and response contracts do not exist.

- [ ] **Step 3: Implement projector, Endpoint and source generation**

Project only code-owned catalog data. Return `Id`, `Title`, `PermissionCode`, `Order`, `Children` and action `Id/Name/PermissionCode/Order`; do not return `ComponentKey`, local file paths or executable metadata.

- [ ] **Step 4: Freeze OpenAPI and run GREEN**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~AuthorizationTreeProjectorTests
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~IdentityRoleManagementAssertions|FullyQualifiedName~OpenApiAuthorizationTreeContractAssertions"
pnpm test:openapi
```

Expected: Unit, selected dual-provider Integration/OpenAPI checks pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Modules/Full.NET.Modules.Identity.Contracts src/Modules/Full.NET.Modules.Identity contracts/openapi/identity-authorization-tree-v1.json tests/Full.NET.UnitTests/Identity tests/Full.NET.IntegrationTests tests/openapi
git commit -m "feat(identity): expose role authorization tree"
```

### Task 4: 在角色权限写入中保护页面/操作父子不变量

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/HostRoleManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/IdentityErrorCodes.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Resources/IdentityErrors.resx`
- Modify: `src/Modules/Full.NET.Modules.Identity/Resources/IdentityErrors.en-US.resx`
- Create: `tests/Full.NET.UnitTests/Identity/HostRoleManagementServiceTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Identity/IdentityRoleManagementAssertions.cs`

**Interfaces:**
- Rejects: action permission without the parent page permission
- Error code: `identity.roles.action_requires_page`
- Preserves: optimistic version, system-role protection and unknown permission rejection

- [ ] **Step 1: Write RED service and dual-provider tests**

Submit `identity.users.reset_password` without `identity.users.read` and assert a validation failure with `identity.roles.action_requires_page`; submit both and assert exact persisted codes.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~HostRoleManagementServiceTests
```

Expected: the orphan action is currently accepted.

- [ ] **Step 3: Implement validation before transaction start**

Build an ordinal lookup from `AuthorizationCatalog.Actions` to the referenced navigation permission. Reject the request when any selected action lacks its page permission. Do not auto-add permissions server-side because that would hide client/request errors.

- [ ] **Step 4: Run GREEN and commit**

Run selected Unit and Identity role Integration for SQL Server/MySQL. Expected: orphan rejected, valid pair persisted, existing protection tests pass.

```powershell
git add src/Modules/Full.NET.Modules.Identity tests/Full.NET.UnitTests/Identity/HostRoleManagementServiceTests.cs tests/Full.NET.IntegrationTests/Identity/IdentityRoleManagementAssertions.cs
git commit -m "fix(identity): enforce page action grant hierarchy"
```

### Task 5: 成对迁移存量 Users 权限

**Files:**
- Create after reservation check: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/054_IdentityUserActionPermissions.sql`
- Create after reservation check: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/054_IdentityUserActionPermissions.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration054IdentityUserActionPermissionsRecoveryTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/SqlServerMigrationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/MySqlMigrationTests.cs`

**Interfaces:**
- Expands role and API Key `identity.users.write` to six exact action permissions
- Removes only the old Users write permission after the complete replacement is present
- Preserves unrelated and already-new permissions without duplicates

- [ ] **Step 1: Confirm reservation and write RED recovery tests**

```powershell
Get-ChildItem src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/054_*.sql
Get-ChildItem src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/054_*.sql
```

Expected before creation: neither provider has a 054 file. If either command finds a file, stop and coordinate a new paired number.

Recovery tests must seed: old-only role, mixed old/new role, unrelated permission, old-only API Key JSON, mixed API Key JSON, and a partially expanded but unrecorded migration.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter FullyQualifiedName~Migration054IdentityUserActionPermissionsRecoveryTests
```

Expected: migration file/behavior is missing.

- [ ] **Step 3: Implement idempotent provider-specific migration**

For role rows, insert each missing new permission with anti-join/`NOT EXISTS`, then delete only `identity.users.write`. For API Key JSON, parse to rows using provider-native JSON functions, union the six replacements, ordinal-sort, write normalized JSON, and leave rows without the old code unchanged. Every step must converge when DbUp has not recorded the script but preceding statements already committed.

- [ ] **Step 4: Run dual-provider recovery and migration GREEN**

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Migration054IdentityUserActionPermissionsRecoveryTests|FullyQualifiedName~SqlServerMigrationTests|FullyQualifiedName~MySqlMigrationTests"
pnpm test:naming
pnpm test:sql-safety
```

Expected: both providers pass recovery, rerun and final-shape assertions; naming and SQL safety pass.

- [ ] **Step 5: Commit**

```powershell
git add src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations tests/Full.NET.IntegrationTests/Migrations
git commit -m "feat(migrations): expand host user action permissions"
```

### Task 6: 增加客户端授权树契约与 Vue 权限门

**Files:**
- Create: `packages/client-contracts/src/authorization-tree.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Create: `packages/client-contracts/tests/authorization-tree.test.ts`
- Create: `ui/admin/src/auth/permission.ts`
- Create: `ui/admin/src/auth/permission.test.ts`
- Create: `ui/admin/src/components/PermissionGate.vue`
- Create: `ui/admin/src/components/PermissionGate.test.ts`
- Modify: `ui/admin/src/api/roles.ts`
- Modify: `ui/admin/src/api/roles.test.ts`

**Interfaces:**
- Produces: `AuthorizationTreePage`, `AuthorizationTreeAction`, `isAuthorizationTreePageArray`
- Produces: `usePermission().can(code)` and `PermissionGate` prop `code: string`
- Consumes: authenticated `session.permissions`; missing/unknown data returns false

- [ ] **Step 1: Write RED runtime-validation and component tests**

Test malformed arrays, duplicate/empty IDs, absent permissions, authenticated single permission, permission revocation and default-slot DOM absence.

```ts
it('does not render the slot without the exact permission', () => {
  const wrapper = mount(PermissionGate, {
    props: { code: 'identity.users.reset_password' },
    slots: { default: '<button>reset</button>' }
  });
  expect(wrapper.find('button').exists()).toBe(false);
});
```

- [ ] **Step 2: Run RED**

```powershell
pnpm --filter @fullnet/client-contracts test -- authorization-tree.test.ts
pnpm --filter @fullnet/admin test -- permission.test.ts PermissionGate.test.ts
```

Expected: files/modules are missing.

- [ ] **Step 3: Implement strict contracts and response permission gate**

Runtime validation must reject executable fields and malformed nesting. `PermissionGate` must derive from the Pinia Session store and return no subtree until the exact permission exists.

- [ ] **Step 4: Run GREEN and commit**

Run both Task 6 commands and `pnpm --filter @fullnet/client-contracts build`. Expected: all selected tests and TypeScript build pass.

```powershell
git add packages/client-contracts ui/admin/src/auth ui/admin/src/components ui/admin/src/api/roles.ts ui/admin/src/api/roles.test.ts
git commit -m "feat(admin): add reactive action permission gate"
```

### Task 7: 将角色授权页改为页面/操作树

**Files:**
- Modify: `ui/admin/src/views/RolesView.vue`
- Create: `ui/admin/src/views/RolesView.test.ts`
- Modify: `packages/admin-i18n/src/messages.ts`
- Modify: `packages/admin-i18n/tests/i18n.test.ts`

**Interfaces:**
- Consumes: `GET /api/v1/identity/authorization-tree`
- Produces: exact page/action permission set to existing `replaceHostRolePermissions`
- Enforces client UX: action selects page; page removal clears descendant actions

- [ ] **Step 1: Write RED Vue tests**

Cover tree rendering, page-only grant, single-action grant, action auto-selecting page, page deselection clearing actions, future unknown stored permission blocking save, and no permission-management button without the current `identity.roles.write` permission. The following Identity Roles wave replaces this coarse gate with `identity.roles.assign_permissions`; do not claim Roles action granularity complete in the Users slice.

- [ ] **Step 2: Run RED**

```powershell
pnpm --filter @fullnet/admin test -- RolesView.test.ts
```

Expected: current flat `HOST_ROLE_ASSIGNABLE_PERMISSIONS` checkbox list fails the hierarchy assertions.

- [ ] **Step 3: Implement tree projection and selection rules**

Remove the hard-coded assignable permission list from the View. Keep the shared runtime parser, render local trusted text only, and submit a sorted exact set. Preserve role version and current system/super-administrator protections.

- [ ] **Step 4: Run GREEN and commit**

```powershell
pnpm --filter @fullnet/admin test -- RolesView.test.ts
pnpm --filter @fullnet/admin build
```

Expected: tests and production build pass.

```powershell
git add ui/admin/src/views/RolesView.vue ui/admin/src/views/RolesView.test.ts ui/admin/src/i18n/locales
git commit -m "feat(admin): grant page and action permissions in role tree"
```

### Task 8: 将 Users 页面所有业务按钮改为独立权限

**Files:**
- Modify: `ui/admin/src/views/UsersView.vue`
- Create: `ui/admin/src/views/UsersView.test.ts`
- Modify: `packages/client-contracts/src/host-roles.ts`
- Modify: `packages/client-contracts/tests/host-roles.test.ts`

**Interfaces:**
- Create form: `identity.users.create`
- Edit: `identity.users.update`
- Assign/save roles: `identity.users.assign_roles`
- Reset password: `identity.users.reset_password`
- Disable: `identity.users.disable`
- Enable: `identity.users.enable`
- Export: `identity.users.export`

- [ ] **Step 1: Write RED exact visibility tests**

Mount one case per permission with page read always present. Assert only the corresponding business control exists; assert read-only users can see the directory but no action control; assert cancel/close controls remain local UI controls.

- [ ] **Step 2: Run RED**

```powershell
pnpm --filter @fullnet/admin test -- UsersView.test.ts
pnpm --filter @fullnet/client-contracts test -- host-roles.test.ts
```

Expected: current `identity.users.write` gate exposes multiple operations and tests fail.

- [ ] **Step 3: Replace coarse gates with `PermissionGate`/exact computed permissions**

Do not invoke protected API methods from an invisible action path. Preserve API error handling, loading state and field projection behavior.

- [ ] **Step 4: Run GREEN and commit**

Run Task 8 commands and Vue production build. Expected: exact visibility and client contract tests pass.

```powershell
git add ui/admin/src/views/UsersView.vue ui/admin/src/views/UsersView.test.ts packages/client-contracts/src/host-roles.ts packages/client-contracts/tests/host-roles.test.ts
git commit -m "feat(admin): enforce exact host user button permissions"
```

### Task 9: 完成直接 API 403、权限撤销与 Vue 真实栈验收

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Identity/IdentityUserRolesManagementAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Identity/IdentityRoleManagementAssertions.cs`
- Modify: `tests/e2e/admin-real-stack/tests/host-users.spec.mjs`
- Modify: `tests/e2e/admin-real-stack/tests/host-roles.spec.mjs`
- Modify: `tests/e2e/admin-real-stack/tests/permission-denied.spec.mjs`
- Create: `docs/verification/vue-action-authorization-2026-08-02.md`
- Modify: `eng/testing/test-matrix.json` only after fresh test discovery
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Verifies: page-only, each single action, direct unauthorized API, revocation refresh, Host scope and super-administrator invariants
- Preserves: standard ProblemDetails and stable denial code

- [ ] **Step 1: Write failing Integration/E2E scenarios**

For each action create a role with page read plus exactly that action, verify its API succeeds, and verify adjacent actions return `403`. Browser tests must assert unauthorized buttons are absent, not merely disabled.

- [ ] **Step 2: Run focused RED/GREEN loops**

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~IdentityUserRolesManagementAssertions|FullyQualifiedName~IdentityRoleManagementAssertions"
pnpm --dir tests/e2e/admin-real-stack test -- host-users.spec.mjs host-roles.spec.mjs permission-denied.spec.mjs
```

Expected after Tasks 1–8: selected SQL Server/MySQL and Vue browser scenarios pass; any direct API bypass remains a release blocker.

- [ ] **Step 3: Run affected slice and final governance**

```powershell
pnpm test:integration:affected:plan -- --snapshot admin-action-permissions-identity-users-20260802 --phase inner
pnpm test:integration:affected -- --snapshot admin-action-permissions-identity-users-20260802 --phase slice
pnpm test:naming
pnpm test:sql-safety
pnpm test:governance
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin build
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release
git diff --check
```

Expected: every command exits 0 with non-zero test discovery; affected teardown leaves shared runner and Docker residual at zero.

- [ ] **Step 4: Record fresh evidence and matrix**

Write only actual commands/results to the verification record. Update `eng/testing/test-matrix.json` from fresh discovery; do not copy test counts into plans or README files. Mark “菜单与按钮权限管理” `Build-verified` only after both providers, direct 403 and Vue E2E pass.

- [ ] **Step 5: Commit**

```powershell
git add tests docs/verification/vue-action-authorization-2026-08-02.md docs/roadmap eng/testing/test-matrix.json
git commit -m "test(identity): verify end-to-end action authorization"
```

### Task 10: 启动全后台权限清零队列

**Files:**
- Create: `docs/roadmap/admin-action-permission-inventory.md`
- Modify: `docs/superpowers/plans/2026-07-30-adminnet-design-absorption-program.md`
- Test: `tests/Full.NET.ArchitectureTests/EndpointAuthorizationTests.cs`

**Interfaces:**
- Produces: per-resource inventory with page permission, every server-backed Vue action, Endpoint, current permission, target permission and migration need
- Exit condition: no active Vue business action uses an unregistered or multi-action coarse permission

- [ ] **Step 1: Generate a reviewed inventory from source**

Inventory these waves in order: Identity; Tenancy/Organization; Settings/Auditing; Files/Notifications/Jobs/CodeGeneration; Document and later modules. Every row names the exact Vue component, API function, Endpoint source and target permission code. Local-only controls are explicitly marked `local-ui` and need no permission.

- [ ] **Step 2: Add the forward-only architecture gate**

The gate must reject new Endpoint bindings to a catalog permission marked legacy multi-action. Existing legacy bindings enter the inventory with a removal wave; no directory-wide exclusion or wildcard debt is allowed.

- [ ] **Step 3: Create one dated vertical-slice plan per wave**

Each wave repeats Tasks 2, 4, 5, 8 and 9 for its exact resources and may be reviewed/merged independently. Do not batch unrelated migrations or expand a legacy permission into high-risk actions without documenting the compatibility mapping.

- [ ] **Step 4: Verify the inventory and commit**

```powershell
rg -n "\.write['\"]|Permissions\.Write" ui/admin/src src/Modules
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter FullyQualifiedName~EndpointAuthorizationTests
git diff --check
```

Expected: every reported active binding is either already exact or appears as an owned inventory row with a dated removal wave; Architecture Tests pass.

```powershell
git add docs/roadmap/admin-action-permission-inventory.md docs/superpowers/plans/2026-07-30-adminnet-design-absorption-program.md tests/Full.NET.ArchitectureTests/EndpointAuthorizationTests.cs
git commit -m "docs(authorization): schedule exact permission rollout"
```

---

## Completion Gate

The Identity Users slice is complete only when:

- role grants show page and individual Users actions;
- Vue read-only users see the page and no business action controls;
- each single action grant exposes exactly one action;
- direct adjacent APIs return `403 authorization.permission_denied`;
- role and API Key old permissions are expanded on SQL Server/MySQL and recovery reruns converge;
- unknown permissions and undeclared Endpoint authorization fail Architecture Tests;
- `ui/admin-layui/**` has no diff;
- affected slice, Vue build/test, client contracts, Architecture, naming, SQL safety and governance are fresh GREEN;
- the full-module inventory owns every remaining coarse permission and prevents false “all buttons complete” claims.
