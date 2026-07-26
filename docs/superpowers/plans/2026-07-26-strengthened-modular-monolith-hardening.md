# Strengthened Modular Monolith Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 加固 Full.NET 模块化单体的数据所有权、依赖方向、租户缓存一致性与 API Key 热路径，并用自动化门禁阻止边界继续退化。

**Architecture:** 保留 API、Worker、Migrator 角色分离和共享数据库，不新增微服务、Reporting 项目或数据库对象。租户变更通过同事务 Outbox 表达可靠事实，当前 API 节点只在提交后修复本地缓存；现存跨模块只读 SQL 进入精确债务登记，新门禁禁止新增未登记访问；Identity 使用消费方拥有的机构校验 Port，消除对 Organization.Contracts 的反向依赖。

**Tech Stack:** .NET 10、Dapper、MessagePack-CSharp、FusionCache、Redis Backplane、Microsoft.Testing.Platform、MSTest、SQL Server 2022、MySQL 8。

## Global Constraints

- Full.NET 1.0 保持强化型模块化单体，不新增网络边界或分布式事务。
- 业务模块继续只通过 `IQueryExecutor`、`ICommandExecutor`、`ICommandTransaction` 和 `IOutboxWriter` 使用 Dapper 基础设施。
- 租户变更与 Outbox 必须同事务；缓存删除只能在数据库提交后执行，跨节点失效由 Worker 可靠发布。
- 新增稳定事件类型使用 `fullnet.tenancy.tenant.changed`，SchemaVersion 为 `1`，MessagePack Key 只能追加。
- 不删除或静默更改现有 Organization.Contracts 公共类型；本轮通过新增消费方 Port 迁移仓库内部依赖。
- 不增加数据库迁移；SQL Server/MySQL 使用相同运行时 SQL 语义。
- 所有行为变更先建立失败测试，所有手写代码注释使用中文。

---

### Task 1: Reliable Post-Commit Tenant Change Invalidation

**Files:**
- Create: `src/Modules/Full.NET.Modules.Tenancy/Contracts/TenantChangedIntegrationEvent.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/TenantChangedCacheInvalidationHandler.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/TenantCacheInvalidator.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ManageHostTenants/HostTenantManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/TenantProvisioningService.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantProvisionedCacheInvalidationHandler.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Test: `tests/Full.NET.UnitTests/Tenancy/HostTenantCacheInvalidationTests.cs`
- Test: `tests/Full.NET.UnitTests/Tenancy/TenantChangedCacheInvalidationHandlerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Tenancy/TenancyHostTenantManagementAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Caching/CacheConsistencyTests.cs`

**Interfaces:**
- Produces: `TenantChangedIntegrationEvent(Guid TenantId, string Domain)`
- Produces: `TenantCacheInvalidator.InvalidateLocalAsync(Guid tenantId, string domain)` and `InvalidateDistributedAsync(Guid tenantId, string domain, CancellationToken)`
- Consumes: existing transaction Outbox and FusionCache Backplane behavior.

- [ ] **Step 1: Write failing unit and integration tests**

  Add tests proving that a successful name update and disable each write one `fullnet.tenancy.tenant.changed` Outbox message, failed/version-conflict operations write none, and cache invalidation is not invoked before `ICommandTransaction.ExecuteAsync` returns. Add a handler test proving Backplane exceptions propagate. Extend cache consistency coverage so a secondary node observes the updated name and disabled state after Worker processing.

- [ ] **Step 2: Run tests to verify RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~HostTenantCacheInvalidationTests|FullyQualifiedName~TenantChangedCacheInvalidationHandlerTests"
  ```

  Expected: failure because the changed event, handler and post-commit invalidator do not exist.

- [ ] **Step 3: Implement the event and shared invalidator**

  Add the versioned MessagePack contract:

  ```csharp
  [MessagePackObject]
  public sealed record TenantChangedIntegrationEvent(
      [property: Key(0)] Guid TenantId,
      [property: Key(1)] string Domain);
  ```

  `InvalidateLocalAsync` must use `SkipBackplaneNotifications = true` and `CancellationToken.None`. `InvalidateDistributedAsync` must disable background L2/Backplane operations, rethrow distributed-cache and Backplane exceptions, then remove ID/domain keys and tenant/domain tags.

- [ ] **Step 4: Move management invalidation after commit**

  Inject `IOutboxWriter` into `HostTenantManagementService`. Write `fullnet.tenancy.tenant.changed` inside the update/disable transaction only after the business update succeeds. Change public `UpdateAsync` and `DisableAsync` methods to await the transaction, then call `InvalidateLocalAsync` only for successful results. Remove the current pre-commit FusionCache calls from `DisableCoreAsync`.

- [ ] **Step 5: Register the reliable handler and reuse the invalidator**

  Register `TenantCacheInvalidator` for API, Migrator and Worker closures as required by its consumers. Register `TenantChangedCacheInvalidationHandler` as `IIntegrationEventHandler`. Refactor the provisioned handler and provisioning service to use the same invalidator without changing the existing provisioned event contract.

- [ ] **Step 6: Verify GREEN**

  Run the focused Unit tests, then SQL Server/MySQL tenant-management and cache-consistency Integration filters. Expected: all focused tests pass and both providers observe changed state after Outbox consumption.

### Task 2: Cross-Module Table Ownership Gate

**Files:**
- Create: `contracts/architecture/module-table-access-debt.json`
- Create: `tests/Full.NET.ArchitectureTests/ModuleTableOwnershipTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj`
- Modify: `docs/architecture/adr/ADR-0002-modular-monolith-evolution.md`

**Interfaces:**
- Produces: exact debt entries `{ sourceModule, table, file, reason, removalMilestone }`.
- Consumes: module directory `Full.NET.Modules.<Name>` and canonical table format `fn_<module>_<entity>`.

- [ ] **Step 1: Write the failing ownership scanner**

  Scan production `.cs` files under `src/Modules`, extract `fn_<module>_<entity>` tokens, infer the owning module from the first table segment and reject accesses where source and owner differ. The test must first fail with the current seven distinct file/table combinations.

- [ ] **Step 2: Verify RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-restore --filter "FullyQualifiedName~ModuleTableOwnershipTests"
  ```

  Expected: failure listing Identity→Tenancy/Auditing/Organization, Organization→Identity and Notifications→Identity accesses.

- [ ] **Step 3: Add exact debt registration**

  Register only the currently observed file/table combinations. Reject wildcard file or table values, duplicate entries, missing reasons, missing removal milestones, stale entries and unregistered accesses. Migration files and test fixtures are outside the scan scope.

- [ ] **Step 4: Verify GREEN and negative fixtures**

  Add an in-memory scanner fixture proving a new cross-module table is rejected and an exact registered access is accepted. Run all Architecture tests; expected total increases by the added tests with zero failures.

### Task 3: Remove the Identity-to-Organization Contract Dependency

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity.Contracts/IIdentityOrganizationUnitDirectory.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/HostRoleDataScopeService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj`
- Modify: `src/Modules/Full.NET.Modules.Organization/TenantUnits/TenantOrganizationUnitDirectory.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/OrganizationModule.cs`
- Modify: affected Unit/Integration test fakes
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`

**Interfaces:**
- Produces: `IIdentityOrganizationUnitDirectory.FindActiveUnitAsync(Guid tenantId, Guid unitId, CancellationToken)`
- Produces: `IdentityOrganizationUnitDirectoryEntry(Guid Id, string Code, string Name)`
- Preserves: existing `ITenantOrganizationUnitDirectory` public contract without deleting or changing it.

- [ ] **Step 1: Write the failing dependency assertion**

  Add an Architecture assertion that the Identity implementation project does not reference `Full.NET.Modules.Organization.Contracts` and that a production module's declared dependencies cover all cross-module Contracts references.

- [ ] **Step 2: Verify RED**

  Run the focused DependencyRules test. Expected: failure on `Full.NET.Modules.Identity.csproj`.

- [ ] **Step 3: Add the consumer-owned Port and adapter**

  Define the web-free Port in Identity.Contracts. Make `TenantOrganizationUnitDirectory` implement both the preserved Organization contract and the new Identity Port, mapping the same query result to their respective records. Register both interfaces to the same scoped implementation.

- [ ] **Step 4: Switch Identity and remove the reverse project reference**

  Change `HostRoleDataScopeService` to consume the Identity Port, remove the Organization.Contracts project reference from Identity, and update test fakes.

- [ ] **Step 5: Verify GREEN**

  Run Unit, Architecture and Identity/Organization dual-provider focused Integration tests. Expected: no reverse project reference and unchanged role data-scope behavior.

### Task 4: Bound API Key Last-Used Writes

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/ApiKeyRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/ApiKeySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Security/ApiKeyAuthenticationService.cs`
- Create: `tests/Full.NET.UnitTests/Identity/ApiKeyAuthenticationServiceTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Identity/IdentityApiKeyAssertions.cs`

**Interfaces:**
- Produces: a fixed internal five-minute observation window.
- Preserves: immediate validation of Key enabled/expiry state and user active/lockout/security-stamp state.

- [ ] **Step 1: Write failing touch-window tests**

  Test that a never-used or older-than-five-minutes Key updates `LastUsedAtUtc`, while a recently used Key authenticates successfully without executing `TouchLastUsed`. Add an Integration assertion that two immediate authenticated requests do not advance the stored timestamp twice.

- [ ] **Step 2: Verify RED**

  Run the focused Unit test. Expected: failure because authentication rows do not carry `LastUsedAtUtc` and every successful request currently writes.

- [ ] **Step 3: Implement bounded writes**

  Select `apiKey.LastUsedAtUtc` in `FindForAuthentication`, add it to `ApiKeyAuthenticationRow`, and execute `TouchLastUsed` only when the value is null or no later than `clock.UtcNow - TimeSpan.FromMinutes(5)`. Add the same cutoff predicate to the UPDATE so concurrent requests cannot create repeated physical writes.

- [ ] **Step 4: Verify GREEN**

  Run focused Unit and SQL Server/MySQL API Key Integration tests. Expected: authentication and revocation semantics remain unchanged while recent use skips the write.

### Task 5: Documentation and Full Verification

**Files:**
- Create: `docs/verification/strengthened-modular-monolith-hardening-2026-07-26.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: test-count thresholds only if discovered counts change
- Review: `rules/rule-evolution.md`
- Review: `rules/skill-evolution.md`

**Interfaces:**
- Consumes: Tasks 1–4 completed code and fresh command output.
- Produces: auditable status distinguishing implemented, build-verified and dual-database verified results.

- [ ] **Step 1: Run static and focused verification**

  Run naming, Unit, Architecture, Compatibility, Release build, and the focused SQL Server/MySQL Integration filters affected by tenancy cache, role data scope and API Key authentication.

- [ ] **Step 2: Run full-risk gates**

  Because tenancy infrastructure, Outbox, cache and authentication changed, run:

  ```powershell
  pnpm test:integration:full
  ```

  Report the exact discovered/passed/failed/skipped totals. Do not substitute the previous 172/172 record.

- [ ] **Step 3: Update verification records**

  Record cache failure/retry evidence, ownership debt count, dependency graph change, API Key write window and all fresh commands. Keep Reporting read-model and production deployment manifests explicitly outside this implementation.

- [ ] **Step 4: Perform governance reviews**

  Execute rule evolution first, then Skill evolution. Only change governance files if the observed evidence crosses an existing threshold.

- [ ] **Step 5: Final repository checks**

  Run `git diff --check`, `git status --short`, and verify the isolated branch remains `codex/strengthened-modular-monolith`. Preserve the main worktree's `.cache/` and `.tmp/` directories.
