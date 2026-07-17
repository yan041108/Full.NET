# Full.NET Tenant Context and Permission Navigation Implementation Plan

> **For Codex:** REQUIRED SKILLS: Use `superpowers:executing-plans` task-by-task, `superpowers:test-driven-development` before every behavior change, `fullnet-module-delivery` for the end-to-end module slice, and `frontend-design` before changing either administration client.

**Goal:** Deliver server-authoritative host-to-tenant context switching, minimum RBAC permissions, dynamic navigation, and equivalent Vue/Layui administration experiences on SQL Server and MySQL.

**Architecture:** Identity owns the authorization catalog, RBAC persistence, dynamic policies, permission claims, navigation projection, and refresh-session context. Tenancy validates tenant choices and delegates the verified context to Identity through public contracts. Authentication runs before tenancy resolution, so protected requests derive their effective tenant only from signed JWT claims. Both clients consume guarded shared contracts and map server navigation keys to a local component whitelist.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API/JwtBearer/Authorization, Dapper, DbUp, MSTest, Testcontainers SQL Server/MySQL, System.Text.Json source generation, Vue 3/Pinia/Vue Router/Vitest, native ES Modules/Layui/Vitest, Playwright.

**Approved design:** [`docs/superpowers/specs/2026-07-17-tenant-context-permission-navigation-design.md`](../specs/2026-07-17-tenant-context-permission-navigation-design.md)

---

## Execution rules

- Work on `codex/tenant-context-navigation` in `.worktrees/tenant-context-navigation`.
- Run the baseline checks in the clean worktree before writing the first production change.
- Before each production change, add the smallest failing test and record why it fails.
- All handwritten C#, TypeScript and JavaScript comments are Chinese; identifiers remain English.
- Do not modify published `001` or `002` migrations; add provider-equivalent `003` migrations.
- Do not accept tenant identity from headers, query strings, form values, LocalStorage, or arbitrary component paths.
- Do not copy Admin.NET.Pro source, schema, styles, or product assets.
- Commit only after the task-specific checks pass; database tasks must pass on both providers.

## Baseline: create the isolated worktree

**Step 1: Verify the shared worktree is clean and ignored**

```powershell
git status --short --branch
git check-ignore -q .worktrees
```

Expected: `main` is clean and `.worktrees` is ignored.

**Step 2: Create and enter the feature worktree**

```powershell
git worktree add .worktrees/tenant-context-navigation -b codex/tenant-context-navigation
Set-Location .worktrees/tenant-context-navigation
dotnet restore Full.NET.slnx
pnpm install --frozen-lockfile
```

**Step 3: Prove the inherited baseline**

```powershell
dotnet build Full.NET.slnx -c Release --no-restore
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-build
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-build
dotnet test tests/Full.NET.CompatibilityTests/Full.NET.CompatibilityTests.csproj -c Release --no-build
pnpm test:workspace
pnpm test:clients
pnpm build:clients
```

Expected: all existing checks pass before feature work begins.

### Task 1: Add the module authorization catalog

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Authorization/AuthorizationCatalog.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Authorization/AuthorizationCatalogValidator.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/IAuthorizationCatalogContributor.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/PermissionDefinition.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/NavigationDefinition.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/IdentityAuthorizationContributor.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/TenancyAuthorizationContributor.cs`
- Create: `tests/Full.NET.UnitTests/Identity/AuthorizationCatalogTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Full.NET.Modules.Tenancy.csproj`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`

**Step 1: Write the failing catalog and dependency tests**

Test exact, ordinal permission codes and assert the aggregate rejects duplicate permission codes, duplicate navigation IDs, missing parents, unknown required permissions, and cycles. Assert Identity still has no Tenancy reference, while `TenancyModule.Dependencies` explicitly contains `IdentityModule`.

```csharp
var catalog = AuthorizationCatalog.Create(
    [new StubContributor(permissions, navigation)]);

Assert.AreEqual(
    "platform.dashboard.read",
    catalog.Permissions.Single().Code);
```

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~AuthorizationCatalogTests"
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj --no-restore --filter "FullyQualifiedName~DependencyRulesTests"
```

Expected: FAIL because the catalog contracts and explicit module dependency do not exist.

**Step 2: Implement the immutable catalog**

Expose stable permission definitions through a public contributor contract. Keep the aggregate immutable and deterministically sorted. Register Identity definitions:

```text
platform.dashboard.read
identity.navigation.read
```

Register Tenancy definitions:

```text
tenancy.tenants.read
tenancy.tenants.switch
```

Add navigation definitions for `overview` and `tenant-context`. The validator must fail during application startup; do not defer invalid definitions until the first request.

**Step 3: Verify and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~AuthorizationCatalogTests"
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj --no-restore --filter "FullyQualifiedName~DependencyRulesTests"
git add src/Modules tests/Full.NET.UnitTests/Identity/AuthorizationCatalogTests.cs tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs
git commit -m "feat: add module authorization catalog"
```

### Task 2: Add provider-equivalent RBAC and session-context schema

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/003_AuthorizationContext.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/003_AuthorizationContext.sql`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/SqlServerMigrationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/MySqlMigrationTests.cs`

**Step 1: Write failing schema and constraint assertions**

For both providers assert:

- `fn_identity_role`, `fn_identity_user_role`, and `fn_identity_role_permission` exist;
- `(ScopeKey, Code)`, `(UserId, RoleId)`, and `(RoleId, PermissionCode)` uniqueness exists;
- foreign keys reject missing users and roles without deleting audit history;
- `fn_identity_refresh_session.ActiveTenantId` and `fn_identity_auth_audit.ContextTenantId` are nullable;
- a second migration run executes zero scripts.

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~MigrationTests"
```

Expected: FAIL because migration `003_AuthorizationContext.sql` is missing.

**Step 2: Implement both forward-only migrations**

Use SQL Server `uniqueidentifier`/`datetimeoffset(7)` and MySQL `char(36)`/UTC `datetime(6)`. Do not add an Identity-to-Tenancy foreign key for `ActiveTenantId`; module isolation is enforced by Tenancy validation and integration tests. Use restrictive RBAC foreign keys and provider-equivalent indexes.

**Step 3: Verify and commit**

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~MigrationTests"
git add src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations tests/Full.NET.IntegrationTests/Migrations
git commit -m "feat: add authorization context schema"
```

### Task 3: Bootstrap the host administrator role and permissions

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentityRoleRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/Bootstrap/IdentityBootstrapService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Contracts/IIdentityBootstrapService.cs`
- Modify: `src/Hosts/Full.NET.Host.Migrator/Program.cs`
- Modify: `tests/Full.NET.UnitTests/Identity/IdentityBootstrapServiceTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/IdentityApiAssertions.cs`

**Step 1: Extend bootstrap tests before production SQL**

Test a new and an existing host user. Both paths must ensure the `host-administrator` system role, synchronize every current Host permission, remove no unknown custom grant, and ensure the user-role assignment. Run bootstrap twice and assert no duplicate row is created.

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~IdentityBootstrapServiceTests"
```

Expected: FAIL because existing-user bootstrap currently returns before role synchronization.

**Step 2: Implement transactional idempotent synchronization**

Extend `IdentitySql` with explicit Host-only statements for role lookup/insert/update, permission grant existence/insert, and user-role assignment. The bootstrap service obtains Host permission codes from `AuthorizationCatalog`, creates the user only when absent, and always performs RBAC synchronization in the same command transaction.

The Migrator must report whether the account was created and whether authorization was synchronized without logging usernames, passwords, or grants.

**Step 3: Verify and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~IdentityBootstrapServiceTests"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IdentityApi"
git add src/Modules/Full.NET.Modules.Identity src/Hosts/Full.NET.Host.Migrator tests/Full.NET.UnitTests/Identity tests/Full.NET.IntegrationTests/Api
git commit -m "feat: bootstrap host authorization"
```

### Task 4: Issue trusted permissions and effective context in every token

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Authorization/PermissionSnapshotReader.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Authorization/IPermissionSnapshotReader.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Security/IdentityClaimTypes.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Security/IAccessTokenIssuer.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Security/JwtAccessTokenIssuer.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Domain/RefreshSession.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/RefreshSessionRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/Login/Handler.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/RefreshSession/Handler.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/GetCurrentUser/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Contracts/CurrentUserResponse.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs`
- Modify: `tests/Full.NET.UnitTests/Identity/JwtAccessTokenIssuerTests.cs`
- Modify: `tests/Full.NET.UnitTests/Identity/LoginHandlerTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/PermissionSnapshotReaderTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/IdentityApiAssertions.cs`

**Step 1: Write failing claim and permission-filter tests**

Assert token output contains exact claims:

```text
fullnet_actor_scope=host
fullnet_scope=host or tenant:{id:N}
fullnet_tenant_id={id} only in tenant context
fullnet_permission={stable code}, repeated and ordinal-sorted
```

The snapshot reader must intersect enabled role grants with the code catalog and ignore unknown, disabled, cross-scope, or duplicate grants. `/api/v1/me` must return `actorScope`, effective `scope`, active `tenantId`, and sorted permissions.

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~JwtAccessTokenIssuerTests|FullyQualifiedName~PermissionSnapshotReaderTests|FullyQualifiedName~LoginHandlerTests"
```

Expected: FAIL because permission persistence and the new claims are not consumed.

**Step 2: Implement permission loading and session propagation**

Add the RBAC join query with `SqlDataScope.HostOnly`. Extend login session inserts with `ActiveTenantId = NULL`. Extend refresh-session reads and replacement inserts to copy `ActiveTenantId`. Sign permissions only after catalog intersection. Preserve current token expiry and signing behavior.

**Step 3: Verify both providers and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Identity"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IdentityApi"
git add src/Modules/Full.NET.Modules.Identity tests/Full.NET.UnitTests/Identity tests/Full.NET.IntegrationTests/Api
git commit -m "feat: issue trusted authorization claims"
```

### Task 5: Enforce dynamic policies and project navigation

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Authorization/FullNetPermissionRequirement.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Authorization/FullNetPermissionHandler.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Authorization/FullNetPermissionPolicyProvider.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Authorization/AuthorizationEndpointExtensions.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/NavigationNodeResponse.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/GetNavigation/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/GetNavigation/NavigationProjector.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs`
- Create: `tests/Full.NET.UnitTests/Identity/FullNetPermissionHandlerTests.cs`
- Create: `tests/Full.NET.UnitTests/Identity/NavigationProjectorTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/IdentityApiAssertions.cs`

**Step 1: Write failing exact-policy and tree-projection tests**

Assert policy names use `FullNET.Permission:<code>`, comparisons are ordinal, and prefixes/case variants fail. Assert navigation hides unauthorized leaves, removes empty parents, preserves authorized ancestors, sorts by `Order` then ID, and never returns an arbitrary component path.

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~FullNetPermissionHandlerTests|FullyQualifiedName~NavigationProjectorTests"
```

Expected: FAIL because dynamic policies and navigation projection are absent.

**Step 2: Implement and map `/api/v1/navigation`**

Register the custom `IAuthorizationPolicyProvider` without replacing unrelated default policies. Map the Endpoint with explicit `identity.navigation.read`. Return a source-generated `NavigationNodeResponse[]`; filter using signed claims on the server. Component keys are stable identifiers such as `overview` and `tenant-context`, never URLs or file paths.

**Step 3: Verify and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~FullNetPermissionHandlerTests|FullyQualifiedName~NavigationProjectorTests"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IdentityApi"
git add src/Modules/Full.NET.Modules.Identity tests/Full.NET.UnitTests/Identity tests/Full.NET.IntegrationTests/Api
git commit -m "feat: add permission navigation api"
```

### Task 6: Deliver available-tenant and session-context APIs

**Files:**

- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/IIdentitySessionContextService.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/VerifiedTenantContext.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/TenantContextTokenResponse.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/ChangeSessionContext/IdentitySessionContextService.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Contracts/TenantContextSummary.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Contracts/ChangeTenantContextRequest.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/GetAvailableTenants/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/ChangeTenantContext/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/ITenantResolver.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantResolver.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Serialization/TenancyJsonSerializerContext.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs`
- Create: `tests/Full.NET.UnitTests/Identity/IdentitySessionContextServiceTests.cs`
- Create: `tests/Full.NET.UnitTests/Tenancy/TenantContextEndpointTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/TenancyApiAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/TenancyApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/TenancyApiMySqlTests.cs`

**Step 1: Write failing service and provider API tests**

Cover:

- only active tenants are returned, sorted by name/identifier/ID;
- switching requires a Host actor and exact switch permission;
- missing, disabled, or malformed tenants return stable ProblemDetails;
- `SessionId + UserId + Version + active` conditional update has exactly one winner;
- consumed/revoked/wrong-owner sessions return `identity.session_not_active`;
- an active version race returns `identity.session_context_conflict`;
- switching returns a new Access Token, refresh retains the tenant, and `tenantId: null` returns Host.

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~IdentitySessionContextServiceTests|FullyQualifiedName~TenantContextEndpointTests"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~TenancyApi"
```

Expected: FAIL because the context contracts, SQL, and Endpoints are absent.

**Step 2: Implement the cross-module boundary**

Tenancy performs the global active-tenant lookup and passes only `VerifiedTenantContext` to Identity. Identity revalidates `sub`, `sid`, actor scope, permission, owner, and active session before the conditional update. Insert an auth audit with `ContextTenantId`, issue a new token from the unchanged role permissions, and return:

```json
{
  "accessToken": "...",
  "tokenType": "Bearer",
  "expiresAtUtc": "...",
  "context": {
    "tenantId": null,
    "identifier": "host",
    "name": "Host",
    "scope": "host"
  }
}
```

Map `GET /api/v1/tenancy/available` and `PUT /api/v1/tenancy/context` with explicit permission policies. Never accept tenant context from a generic header.

**Step 3: Verify both providers and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~IdentitySessionContextServiceTests|FullyQualifiedName~TenantContextEndpointTests"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~TenancyApi|FullyQualifiedName~IdentityApi"
git add src/Modules tests/Full.NET.UnitTests tests/Full.NET.IntegrationTests
git commit -m "feat: add tenant session context api"
```

### Task 7: Make authenticated tenant resolution claim-authoritative

**Files:**

- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantResolutionMiddleware.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/ITenantResolver.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantResolver.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Modify: `tests/Full.NET.UnitTests/Tenancy/TenantResolverTests.cs`
- Create: `tests/Full.NET.UnitTests/Tenancy/TenantResolutionMiddlewareTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/TenancyApiAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs`

**Step 1: Write failing middleware-order and mismatch tests**

Assert authenticated Host requests without a tenant Claim resolve Host only on a Host domain; signed tenant Claims resolve by active tenant ID; non-Host domains must match the resolved tenant domain; disabled/missing tenants and mismatches return `403 tenancy.context_mismatch`. Assert `X-Tenant-Id`, query strings, and request bodies never change `ICurrentTenant`.

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~TenantResolutionMiddlewareTests|FullyQualifiedName~TenantResolverTests"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~TenancyApi"
```

Expected: FAIL because tenancy currently runs before authentication and resolves only by host name.

**Step 2: Implement the trusted resolution order**

Set the pipeline to:

```csharp
app.UseExceptionHandler();
app.UseCors(IdentityModule.BrowserCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseFullNetTenancy();
app.UseAuthorization();
```

Keep request logging outermost. Anonymous requests continue domain resolution. Clear `CurrentTenantAccessor` in `finally` for every path.

**Step 3: Verify and commit**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Tenancy"
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~TenancyApi|FullyQualifiedName~IdentityApi"
git add src/Hosts/Full.NET.Host.Api src/Modules/Full.NET.Modules.Tenancy tests
git commit -m "fix: trust signed tenant context"
```

### Task 8: Publish guarded shared browser contracts

**Files:**

- Create: `packages/client-contracts/src/authorization.ts`
- Create: `packages/client-contracts/src/tenancy.ts`
- Create: `packages/client-contracts/tests/authorization.test.ts`
- Create: `packages/client-contracts/tests/tenancy.test.ts`
- Modify: `packages/client-contracts/src/identity.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Modify: `packages/client-contracts/tests/identity.test.ts`

**Step 1: Write failing runtime-guard tests**

Test valid and malformed navigation trees, duplicate IDs, unknown parent shapes, invalid component keys, tenant summaries, context-token responses, and `/me.actorScope`. Guards must reject partial data and return `false`; they must not mutate or sanitize untrusted responses silently.

```powershell
pnpm --filter @fullnet/client-contracts test
```

Expected: FAIL because the new contracts and actor scope guard are absent.

**Step 2: Implement types and strict guards**

Export `NavigationNode`, `TenantContextSummary`, `TenantContextTokenResponse`, and exact guards. Keep the component key syntactically constrained here; each client still applies its own semantic whitelist.

**Step 3: Verify and commit**

```powershell
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/client-contracts build
git add packages/client-contracts
git commit -m "feat: add authorization client contracts"
```

### Task 9: Replace Vue static navigation and tenant demo data

**Files:**

- Create: `ui/admin/src/navigation/catalog.ts`
- Create: `ui/admin/src/navigation/catalog.test.ts`
- Create: `ui/admin/src/views/TenantContextView.vue`
- Create: `ui/admin/src/views/TenantContextView.test.ts`
- Modify: `ui/admin/src/auth/session.ts`
- Modify: `ui/admin/src/auth/session.test.ts`
- Modify: `ui/admin/src/router/index.ts`
- Modify: `ui/admin/src/App.vue`
- Modify: `ui/admin/src/App.test.ts`
- Modify: `ui/admin/src/styles/app.css`

**Step 1: Write failing Pinia, whitelist, and interaction tests**

Test login/restore loads `/me`, `/navigation`, then available tenants when permitted; unknown component keys reject the navigation snapshot; `can(permission)` is exact; successful switching replaces the token before reloading state; a failed switch keeps the original context; a no-longer-authorized route redirects to `/403` or the first accessible node.

```powershell
pnpm --filter @fullnet/admin test
```

Expected: FAIL because Vue still uses static navigation and demo tenant text.

**Step 2: Implement the Vue experience**

Apply `frontend-design` guidance while preserving existing Full.NET tokens and shell. Map only local `overview` and `tenant-context` components. Render server titles/captions as text, add an accessible top/side tenant selector, show pending state without optimistic label changes, and expose `can(permission)` for buttons. Do not store tenant or Access Token in Web Storage.

**Step 3: Verify and commit**

```powershell
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin typecheck
pnpm --filter @fullnet/admin build
git add ui/admin
git commit -m "feat: add Vue tenant context navigation"
```

### Task 10: Match the feature in the native Layui administration client

**Files:**

- Create: `ui/admin-layui/js/core/contracts.js`
- Create: `ui/admin-layui/js/core/navigation.js`
- Create: `ui/admin-layui/tests/contracts.test.js`
- Create: `ui/admin-layui/tests/navigation.test.js`
- Modify: `ui/admin-layui/js/core/session.js`
- Modify: `ui/admin-layui/js/app.js`
- Modify: `ui/admin-layui/index.html`
- Modify: `ui/admin-layui/css/app.css`
- Modify: `ui/admin-layui/tests/session.test.js`
- Modify: `ui/admin-layui/tests/app.test.js`
- Modify: `tests/e2e/admin-parity/tests/shell-parity.spec.mjs`

**Step 1: Write failing native-state and DOM-safety tests**

Cover the same state transitions as Vue. Assert navigation and tenant values are inserted through `createElement`/`textContent`, unknown component keys are rejected, Hash routes are restricted to local views, `data-permission` visibility is exact, switching failure keeps the old context, and cleanup removes all listeners.

```powershell
pnpm --filter @fullnet/admin-layui test
pnpm --filter @fullnet/admin-parity-e2e test
```

Expected: FAIL because Layui still renders static navigation and demo tenant data.

**Step 2: Implement equal Layui functionality**

Apply `frontend-design` guidance without introducing Vue, React, or another SPA runtime. Render the same workbench and tenant-context capabilities using native ES Modules and the existing Layui-enhanced shell. Preserve keyboard focus, pending/disabled states, ProblemDetails feedback, and teardown behavior.

**Step 3: Verify production dependency boundaries and commit**

```powershell
pnpm --filter @fullnet/admin-layui test
pnpm --filter @fullnet/admin-layui build
pnpm --filter @fullnet/admin-parity-e2e test
pnpm --filter @fullnet/admin-layui test
git add ui/admin-layui tests/e2e/admin-parity
git commit -m "feat: add Layui tenant context navigation"
```

### Task 11: Complete parity E2E, documentation, evolution audits, and release verification

**Files:**

- Modify: `tests/e2e/admin-parity/tests/shell-parity.spec.mjs`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify when thresholds are met: `rules/*.md`
- Modify when thresholds are met: `.agents/skills/**`

**Step 1: Add the final failing parity scenarios**

Run both clients through login, dynamic navigation, tenant entry, current-tenant display, refresh recovery, Host return, unknown component rejection, and 403. Use route interception only for client rendering cases; keep real Host integration coverage for CORS, authentication, authorization, and tenant middleware ordering.

```powershell
pnpm --filter @fullnet/admin-parity-e2e test
```

Expected: FAIL until both clients expose the complete equivalent flow.

**Step 2: Update durable documentation**

Document migrations, claims, Endpoint contracts, deployment order, local verification, and the remaining C2.1 CRUD slices. Mark only evidence-backed roadmap items as `Verified`; keep internationalization and unimplemented CRUD explicitly open. Update test counts from fresh output.

**Step 3: Perform required rule and Skill evolution audits**

Read and execute:

```text
rules/rule-evolution.md
rules/skill-evolution.md
```

Record every observed omission against the existing prevention rules. Update a rule only if its threshold and conflict checks are satisfied. Update or add a project Skill only after its candidate threshold is satisfied and its contract test is written first.

**Step 4: Run fresh full verification**

```powershell
dotnet restore Full.NET.slnx --locked-mode
dotnet build Full.NET.slnx -c Release --no-restore
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-build
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-build
dotnet test tests/Full.NET.CompatibilityTests/Full.NET.CompatibilityTests.csproj -c Release --no-build
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-build
pnpm install --frozen-lockfile
pnpm test:workspace
pnpm test:clients
pnpm build:clients
pnpm test:e2e
python tests/skills/validate_project_skills.py
git diff --check
git status --short --branch
```

Expected: every command passes; SQL Server and MySQL integration classes both execute; no secret, generated artifact, or unrelated user change is staged.

**Step 5: Review, commit, and integrate**

Use `superpowers:requesting-code-review` and resolve every confirmed issue with a failing regression test. Then:

```powershell
git add README.md docs tests rules .agents/skills
git commit -m "docs: verify tenant navigation delivery"
```

Use `superpowers:finishing-a-development-branch` to merge the verified branch into local `main`, rerun the risk-focused checks on merged `main`, remove the worktree, and delete `codex/tenant-context-navigation`. Do not push or create a pull request unless the user separately authorizes it.
