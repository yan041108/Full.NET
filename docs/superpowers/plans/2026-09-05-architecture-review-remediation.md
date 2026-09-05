# Architecture Review Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复架构审查发现的模块裁剪、Global SQL 漏扫、租户通知收件人越界、系统模板审计归属和生成文档漂移问题。

**Architecture:** 保持强化型模块化单体和现有 Contracts 边界。Workflow 在发布可靠通知事件时显式依赖 Notifications；架构测试从统一生产模块程序集集合扫描；Notifications 按可信 Scope 调用 Identity 的 Host/Tenant 批量目录；自动模板使用独立系统审计主体。

**Tech Stack:** .NET 10、MSTest、NSubstitute、Dapper SQL 治理、Node.js `node:test`、Mermaid 生成器。

## Global Constraints

- 生产 SQL 只能访问所属模块表，SQL Server 与 MySQL 语义必须一致。
- Global SQL 必须在 `contracts/architecture/global-sql-statements.json` 精确登记，禁止通配豁免。
- 租户身份只来自可信 `ICurrentTenant` 或事件 Envelope，普通请求不得选择 TenantId。
- 后端新增或修改的类型和方法使用中文 XML 文档注释；关键业务边界使用中文意图注释。
- 不修改公共 HTTP/JSON 契约；修复阶段先保留本地变更，后续仅在用户明确授权下提交或合并；完成前执行影响集、Release 构建和 `git diff --check`。

---

### Task 1: Close Workflow notification module selection

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Modify: `tests/Full.NET.UnitTests/Modularity/FullNetModuleCatalogTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/WorkflowModule.cs`

**Interfaces:**
- Consumes: `IFullNetModule.Dependencies` and `FullNetModuleSelection.ResolveEnabledModules(...)`.
- Produces: Workflow runtime dependency set `Identity, Notifications` and a configuration rejection when Notifications is absent.

- [x] Add a test asserting `new WorkflowModule().Dependencies` contains exactly `Identity` and `Notifications`, plus a module-selection test that `Identity + Workflow` fails with the missing Notifications dependency.
- [x] Run the focused tests and confirm they fail because Workflow currently declares only Identity.
- [x] Change `WorkflowModule.Dependencies` to `["Identity", "Notifications"]` and update its XML summary.
- [x] Re-run the focused tests and confirm they pass.

### Task 2: Make production SQL scanning complete

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/GlobalSqlStatementCatalogTests.cs`
- Modify: `contracts/architecture/global-sql-statements.json`

**Interfaces:**
- Consumes: official module implementation projects and `ProductionAssemblies.BusinessModules`.
- Produces: one authoritative module assembly set shared by dependency and SQL governance tests.

- [x] Add a meta-test that compares every non-Contracts `src/Modules/Full.NET.Modules.*` project with `ProductionAssemblies.BusinessModules`.
- [x] Run the meta-test and confirm it reports the currently omitted official module assemblies.
- [x] Centralize all official module implementation assemblies in `ProductionAssemblies.BusinessModules`, spread that set into `All`, and remove the Global SQL test's manual module append list.
- [x] Run the Global SQL test and confirm it fails with unregistered Workflow statements.
- [x] Add one exact `explicit_tenant_anchor` catalog entry per Workflow Global statement, requiring the owned table plus `TenantScopeKey` or the already-scoped parent identifier.
- [x] Re-run the architecture tests and confirm the catalog and construction scanners pass.

### Task 3: Resolve notification recipients by trusted scope in one batch

**Files:**
- Create: `src/Modules/Full.NET.Modules.Notifications/Features/CreateNotificationIntents/NotificationRecipientDirectoryResolver.cs`
- Create: `tests/Full.NET.UnitTests/Notifications/NotificationRecipientDirectoryResolverTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/CreateNotificationIntents/NotificationIntentService.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityDomainServiceCollectionExtensions.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `tests/Full.NET.UnitTests/Notifications/NotificationsModuleRegistrationTests.cs`
- Modify: `tests/Full.NET.UnitTests/Identity/IdentityModuleRegistrationTests.cs`

**Interfaces:**
- Consumes: `IHostUserBatchSelectionDirectory.FindActiveHostUsersAsync(...)` and `ITenantUserSelectionDirectory.FindActiveTenantUsersAsync(...)`.
- Produces: `NotificationRecipientDirectoryResolver.ResolveAsync(scope, recipients, cancellationToken)` returning ordered user identifiers or `notifications.inbox_recipient_not_found`.

- [x] Add resolver tests proving Host uses one Host batch query, Tenant uses one Tenant batch query, and a missing Tenant member fails closed without calling the Host directory.
- [x] Run the focused tests and confirm compilation fails because the resolver does not exist.
- [x] Implement the resolver with scope branching, one batch call, stable input order and exact missing-recipient failure.
- [x] Replace the row-by-row lookup in `NotificationIntentService` with the resolver and register the resolver in API/Worker notification closures.
- [x] Extend Identity's minimal Worker directory registration with the Host batch and Tenant batch contracts required by the projection path.
- [x] Re-run focused service and DI registration tests.

### Task 4: Separate automatic template audit provenance from recipients

**Files:**
- Modify: `tests/Full.NET.UnitTests/Notifications/WorkflowNotificationTemplateProvisionerTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ProjectWorkflowNotifications/WorkflowNotificationTemplateProvisioner.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ProjectWorkflowNotifications/WorkflowNotificationProjectionService.cs`

**Interfaces:**
- Consumes: trusted workflow scope and built-in template key.
- Produces: a stable non-empty `AutomaticProvisionerActorId` used only for automatic template create/publish audit fields.

- [x] Add assertions that automatic template `CreatedById` and version `PublishedById` equal the system actor and differ from the notification recipient.
- [x] Run the test and confirm it fails because both values currently equal the recipient.
- [x] Make the provisioner own a documented stable system actor, remove its actor parameter, and keep the business actor only for Intent creation.
- [x] Re-run provisioner and projection tests.

### Task 5: Enforce and regenerate architecture documents

**Files:**
- Modify: `tests/governance/module-dependency-graph.test.mjs`
- Regenerate: `docs/operations/module-dependency-graph.mmd`
- Modify: `README.md`

**Interfaces:**
- Consumes: `generateModuleDependencyGraph({ write: false })` result and `outputPath`.
- Produces: exact generated-file drift detection and current cache invalidation description.

- [x] Change the governance test to read `outputPath` and compare the committed Mermaid content exactly with generated content.
- [x] Run the test and confirm it fails on the stale committed graph.
- [x] Run `pnpm generate:module-dependency-graph` and re-run the governance test.
- [x] Replace the README's cache Outbox repair statement with commit-after L1/L2 deletion, Redis Backplane and TTL/version convergence.

### Task 6: Verify the complete remediation

**Files:**
- Modify: `docs/superpowers/plans/2026-09-05-architecture-review-remediation.md` only to mark executed steps.

**Interfaces:**
- Consumes: task baseline `bd0f310ee0477f75b58f8234e7850cf4306e62c7`.
- Produces: fresh build, test, governance and diff evidence without claiming environment-heavy verification.

- [x] Run focused Architecture, Unit and Governance tests for every regression.
- [x] Run `pnpm test:integration:affected:plan -- --base bd0f310ee0477f75b58f8234e7850cf4306e62c7 --phase inner` and execute the selected non-container inner checks.
- [x] Run `dotnet build Full.NET.slnx -c Release --no-restore`, `pnpm test:naming`, `pnpm test:sql-safety`, `git diff --check` and inspect `git status`.
- [x] Record SQL Server/MySQL, Worker full-chain and GitHub Actions checks as unverified unless fresh evidence exists.

## Execution Notes

- 完整生产程序集扫描额外暴露了 Jobs 的异步方法命名违规，以及 Notifications/ObservabilityAdmin 多类型源文件无法定位的问题；已修正方法名并让源码定位器支持唯一类型声明回退。
- 新发现的 59 个 Jobs/Notifications 既有公开错误码未静默改名，而是以精确文件、精确值登记到 `contracts/naming/naming-debt.json`，等待 M1.0 版本化兼容迁移。
- 影响集选择器实际进入 Identity/MySQL 容器集后超过 10 分钟无结果且无活动数据库连接，已人工停止并按未验证记录；未将该运行表述为通过。
