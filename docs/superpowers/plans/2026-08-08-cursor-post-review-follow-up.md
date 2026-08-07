# Cursor 多任务审查后续实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. 模块纵向切片同时使用项目 Skill `$fullnet-module-delivery`。一次只执行一个 Task，不并行修改共享模块、迁移号、Integration/Docker 或 `.NET` 输出。

**Goal:** 修复 2026-08-08 Cursor 多任务审查暴露的剩余并发与模块依赖缺口，并把已冻结的 Layui 从活动交付门禁中移出。

**Architecture:** Files 通过同一模块事务、统一行锁顺序和条件 DML 保证 Claim/删除互斥；Identity 的机构投影改用消费方拥有的最小契约，恢复模块依赖 DAG；投影运维只提供可断点、可 dry-run、可对账的有界操作。Vue 保持唯一活动后台，Layui 仅保留冻结扫描和显式例外测试。

**Tech Stack:** .NET 10、ASP.NET Core、Dapper、SQL Server、MySQL、DbUp、MessagePack Outbox、MSTest、Node.js、pnpm、GitHub Actions。

## Global Constraints

- Cursor 开工前必须重新读取 `git rev-parse HEAD` 和 `git status --short --branch`；不得假定本文编写时的工作区仍未变化。
- 每个 Task 使用下表中的独立 snapshot，严格执行 RED → 最小 GREEN → affected plan → affected slice → `git diff --check`。
- 数据库行为必须由 SQL Server/MySQL 同时证明；不得用单 Provider 或纯 Mock 关闭并发、锁和恢复语义。
- 模块依赖图必须保持有向无环；禁止用“反向依赖已存在”作为通用契约引用豁免。
- 不新增跨模块本地事务、跨模块 SQL/外键、网络 RPC、Broker 或分布式事务。
- Layui 目录保持零功能 diff；Task 4 只调整门禁与历史测试归类，不改 Layui 产品代码。
- Task 1–3 修改共享模块，必须串行；Task 4 只能在前三项工作区干净且提交完成后开始。

| 顺序 | Snapshot | Task | 退出门槛 |
|---:|---|---|---|
| 1 | `cursor-review-files-claim-concurrency-20260808` | Files Claim/删除并发原子性 | 双库真实并发矩阵通过，无死锁泄漏或无保护引用 |
| 2 | `cursor-review-module-contract-dag-20260808` | Identity→Organization 契约环退役 | 依赖 DAG 无精确债务，Architecture 负向 fixture 通过 |
| 3 | `cursor-review-org-projection-operations-20260808` | Identity 机构投影运维闭环 | keyset、断点、dry-run、apply、对账、取消和双库通过 |
| 4 | `cursor-review-layui-freeze-ci-20260808` | Layui 活动门禁退役 | 新功能 CI 只验 Vue；冻结扫描继续失败关闭 |

---

### Task 1: Files Claim/删除并发原子性

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Files/Persistence/HostFileSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Persistence/HostFileReferenceClaimSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Features/HostFileReferenceClaims/HostFileReferenceClaimService.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/HostFileManagementService.cs`
- Modify: `tests/Full.NET.UnitTests/Files/HostFileReferenceClaimServiceTests.cs`
- Modify: `tests/Full.NET.UnitTests/Files/HostFileManagementServiceTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Document/DocumentFilesReferenceClaimAssertions.cs`
- Modify after GREEN: `docs/verification/document-files-reference-claim-2026-08-08.md`

**Interfaces:**
- Preserves: `IHostFileReferenceClaimService` public signatures and `pending -> active -> released` wire states.
- Produces: provider-specific file-row lock statements selected by trusted `DatabaseOptions.Provider`.
- Invariant: Claim and Delete acquire the same `fn_files_file` row before inspecting or changing open claims; `released` is terminal and cannot be reused as a successful Claim.

- [ ] **Step 1: Start snapshot and add RED dual-provider race**

```powershell
pnpm test:task:start -- cursor-review-files-claim-concurrency-20260808
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~DocumentFilesReferenceClaim" --no-restore
```

In `DocumentFilesReferenceClaimAssertions`, create two independent DI scopes/connections and synchronize them with `TaskCompletionSource` so one operation calls `ClaimAsync` while the other calls `DeleteAsync` for the same Ready file. Repeat both start orders. The only valid terminal states are:

```text
claim succeeds  => delete returns files.file.referenced and file remains Ready
delete succeeds => claim returns files.file.not_found and no open claim exists
```

Reject timeout, unobserved deadlock, both-success, deleted-file-plus-open-claim, and Ready-file-without-the-successful-claim result. Verify the test fails against a fixture that removes the shared row lock.

- [ ] **Step 2: Add a shared provider lock order**

Add SQL Server and MySQL statements with these semantics:

```sql
-- SQL Server
SELECT Id FROM fn_files_file WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @FileId AND TenantId IS NULL;

-- MySQL
SELECT Id FROM fn_files_file
WHERE Id = @FileId AND TenantId IS NULL
FOR UPDATE;
```

Both Claim and Delete must execute the provider-selected statement inside their Files-local transaction before reading active state/open claims. Keep the conditional `INSERT ... SELECT ... DeletedAtUtc IS NULL` and `UPDATE ... NOT EXISTS(open claim)` as defense in depth. Unknown providers throw before mutation.

- [ ] **Step 3: Lock released-idempotency semantics**

Keep these Unit expectations:

```csharp
Assert.AreEqual(FilesErrorCodes.InvalidClaim, releasedReuse.Error!.Code);
Assert.AreEqual(FilesErrorCodes.FileNotFound, conditionalInsertLostRace.Error!.Code);
```

Also cover duplicate Pending and Active requests returning the same Claim ID, payload conflict failing with `files.claim.payload_conflict`, and Confirm/Release racing without reviving Released.

- [ ] **Step 4: Run GREEN and affected slice**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~HostFileReferenceClaimServiceTests|FullyQualifiedName~HostFileManagementServiceTests" --no-restore
pnpm test:integration:affected:plan -- --snapshot cursor-review-files-claim-concurrency-20260808 --phase slice
pnpm test:integration:affected -- --snapshot cursor-review-files-claim-concurrency-20260808 --phase slice
pnpm test:sql-safety
git diff --check
```

Expected: Unit 和 SQL Server/MySQL Files/Document 影响集非零发现并通过；完成后 Docker/runner residual 为 0。

---

### Task 2: Identity→Organization 契约环退役

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity.Contracts/OrganizationUnitProjectionContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUnits/TenantUnitManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/TenantUnits/OrganizationUnitProjectionCatalog.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/OrganizationModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/OrganizationUnitProjection/**`
- Modify: `src/Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj`
- Delete after all consumers move: `src/Modules/Full.NET.Modules.Organization.Contracts/OrganizationUnitIntegrationEvents.cs`
- Delete after all consumers move: `src/Modules/Full.NET.Modules.Organization.Contracts/IOrganizationUnitProjectionCatalog.cs`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Modify: `tests/Full.NET.UnitTests/Identity/OrganizationUnitProjectionWriterTests.cs`
- Modify: affected serialization/Integration tests

**Interfaces:**
- Produces in consumer-owned `Identity.Contracts`:

```csharp
public interface IIdentityOrganizationUnitProjectionSource
{
    Task<Result<IdentityOrganizationUnitProjectionPage>> ListAsync(
        Guid tenantId,
        Guid? afterUnitId,
        int pageSize,
        CancellationToken cancellationToken = default);
}
```

- Produces a MessagePack event in `Identity.Contracts` containing `TenantId, UnitId, Name, IsActive, Version, ChangedAtUtc`; preserve the current stable `MessageType` and SchemaVersion 1 wire values.
- Invariant: Organization already depends on Identity, so it adapts its owned state to the consumer-defined projection contract; Identity no longer references `Organization.Contracts` and can keep `Dependencies => []` without a hidden reverse edge.

- [ ] **Step 1: Add RED architecture fixtures**

Add a fixture proving an arbitrary `A -> B` reverse dependency does not authorize `B -> A.Contracts`. Add an assertion that every production `.Contracts` reference is represented by the declared DAG and that `AllowedReverseContractDependencies` is empty.

```powershell
pnpm test:task:start -- cursor-review-module-contract-dag-20260808
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter "FullyQualifiedName~Production_module_contract_references_are_declared_dependencies|FullyQualifiedName~Reverse_module_dependency" --no-restore
```

Expected RED: Identity still references Organization.Contracts or the temporary exact debt is non-empty.

- [ ] **Step 2: Move only the consumer-specific wire contract**

Create the source port, page DTO, snapshot DTO and MessagePack event in Identity.Contracts. Organization implements/publishes them but keeps its domain types internal. Do not move generic Organization DTOs, permissions or errors into Identity/BuildingBlocks. Preserve existing MessagePack integer keys and message type exactly.

- [ ] **Step 3: Remove the reverse project reference and exact debt**

After Unit/serialization tests pass, remove the two superseded Organization.Contracts files, remove the Identity project reference to Organization.Contracts, and make `AllowedReverseContractDependencies` empty. The Architecture test must fail if any future pair recreates a bidirectional edge.

- [ ] **Step 4: Verify DAG and both providers**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~OrganizationUnitProjection|FullyQualifiedName~IdentityModuleRegistrationTests" --no-restore
pnpm test:dotnet:architecture
pnpm test:integration:affected -- --snapshot cursor-review-module-contract-dag-20260808 --phase slice
pnpm test:naming
git diff --check
```

Expected: Identity/Organization 双 Provider 通过，模块依赖 DAG 和公开契约引用均无例外。

---

### Task 3: Identity 机构投影运维闭环

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/OrganizationUnitProjectionContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/TenantUnits/OrganizationUnitProjectionCatalog.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationSql.cs`
- Replace: `src/Modules/Full.NET.Modules.Identity/Features/OrganizationUnitProjection/OrganizationUnitProjectionBackfillService.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/OrganizationUnitProjection/OrganizationUnitProjectionReconciliationService.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/OrganizationUnitProjection/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Add Unit and SQL Server/MySQL Integration tests beside existing projection tests
- Create after GREEN: `docs/verification/identity-organization-unit-projection-operations-2026-08-08.md`

**Interfaces:**
- Keyset page: ordered by `UnitId`, accepts exclusive `afterUnitId`, maximum page size 100, returns `NextAfterUnitId` and `HasMore`.
- Reconciliation request: trusted `TenantId`, optional cursor, `PageSize`, `Mode = dry-run | apply`.
- Response: scanned, missing, stale, extra, applied, next cursor and completion flag; no translated text is used as machine state.

- [ ] **Step 1: Add RED keyset and interruption tests**

Cover insertions between pages, cancellation after a completed page, retry from returned cursor, repeated apply, old source version, missing local row, stale local row, and dry-run performing zero commands. Offset/page-number pagination is not accepted as a checkpoint.

- [ ] **Step 2: Implement bounded source and comparison**

Use provider-neutral `WHERE Id > @AfterUnitId ORDER BY Id` with SQL Server `TOP (@PageSize)` and MySQL `LIMIT @PageSize`. Compare one source page with one bounded Identity-local lookup. Apply only missing/newer rows through `OrganizationUnitProjectionWriter`; never delete local rows solely because a partial page omitted them.

- [ ] **Step 3: Expose an explicit Host operation**

Map a Host-only endpoint with its own stable read/apply permissions. `dry-run` may read and report; `apply` mutates the Identity projection and must be separately authorized/audited. One request processes at most 100 rows and returns the next cursor; do not start an unbounded API background loop.

- [ ] **Step 4: Verify both providers and cancellation**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~OrganizationUnitProjection" --no-restore
pnpm test:integration:affected -- --snapshot cursor-review-org-projection-operations-20260808 --phase slice
pnpm test:openapi
pnpm test:naming
git diff --check
```

Expected: SQL Server/MySQL keyset/retry/dry-run/apply 通过，OpenAPI/JSON 源生成和精确权限门禁通过。

---

### Task 4: Layui 活动交付门禁退役

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `package.json`
- Modify: `tests/client-workspace.test.mjs`
- Modify: `tests/governance/layui-freeze.test.mjs`
- Modify: `tests/e2e/admin-real-stack/playwright.config.mjs`
- Split or rename: `tests/e2e/admin-parity/**` only where required to create a Vue-only active suite; do not edit Layui source
- Modify: `tests/performance/frontend-bundle-budgets.json`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `docs/superpowers/plans/2026-07-30-adminnet-design-absorption-program.md`
- Create after GREEN: `docs/verification/layui-active-gate-retirement-2026-08-08.md`

**Interfaces:**
- Produces: `test:e2e:admin` for the active Vue mock/contract suite.
- Preserves: `test:e2e:layui-frozen` as an explicit manual/security/migration exception suite, not a default new-feature gate.
- Invariant: CI still runs `layui-freeze.test.mjs` to reject unauthorized Layui diffs; historical source and reports remain recoverable.

- [ ] **Step 1: Add RED workflow contract tests**

Extend `client-workspace.test.mjs` and `layui-freeze.test.mjs` so CI fails if the default client test/build/E2E commands select `@fullnet/admin-layui`, start its Vite server, or require its bundle budget for a new Vue feature. Also fail if the freeze-governance test is removed.

- [ ] **Step 2: Split active and frozen commands**

Remove `@fullnet/admin-layui` from the default CI test filter, default `build:clients`, bundle budget and real-stack startup. Rename the parity suite/command to Vue-active semantics where it no longer compares two clients. Keep a separately named explicit frozen command for authorized Layui maintenance.

- [ ] **Step 3: Update only authoritative status**

Check Task 12 Step 4 in `2026-07-30-adminnet-design-absorption-program.md` only after workflow tests prove the active gate no longer selects Layui. Do not delete historical verification records or claim Layui source has been retired.

- [ ] **Step 4: Verify**

```powershell
node --test tests/client-workspace.test.mjs tests/governance/layui-freeze.test.mjs
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin build
pnpm test:e2e:admin
pnpm test:governance
git diff --check
```

Expected: Vue active tests/build/E2E 通过，Layui 冻结扫描通过，默认 CI 不再启动或构建 Layui。

## Final Program Gate

Tasks 1–4 全部独立提交后，以 Task 1 开工前基线执行一次 affected merge。必须同时通过 `pnpm test:governance`、`pnpm test:openapi`、`pnpm test:naming`、`pnpm test:sql-safety`、`pnpm test:dotnet:architecture`、`pnpm test:dotnet:unit`、affected merge 和 `git diff --check`。测试数量只按 fresh discovery 更新 `eng/testing/test-matrix.json`。首个真实 Integration Event 非加法版本升级仍保持 Decision Gate，不得为了执行本计划人为制造 v2。
