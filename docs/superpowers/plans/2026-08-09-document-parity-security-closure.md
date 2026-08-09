# Document Parity Security Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 Document 的分类、标签、回收站、权限、分享和统计从“代码已添加”收口为双库可迁移、契约兼容、安全且 Vue 可验收的完整纵向切片。

**Architecture:** 保持 Document 单模块垂直切片和 Dapper 显式 SQL；093 迁移以实际 persistence record/SQL 为唯一形状。匿名分享采用不可逆口令哈希、POST 验证、原子限额计数和低成本限流，管理 API 永不回显凭据；Vue 只在精确权限下创建入口。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper、SQL Server 2022、MySQL 8.4、DbUp、System.Text.Json source generation、Vue 3、TypeScript、Vitest、Playwright。

## Global Constraints

- 不引入 EF Core、通用 Repository 或新的业务模块项目。
- SQL Server/MySQL 的 093 结构、索引、恢复和运行时行为必须成对验证。
- 公开 JSON 契约不得包含口令或口令哈希；现有 C# 构造调用保持源码兼容。
- 匿名访问不得使用 GET 产生计数副作用；口令比较必须抗时序泄漏。
- 所有管理端页面和操作必须同时具备 Vue 入口权限与 Endpoint 精确权限。
- Layui 已冻结，本计划不得修改 `ui/admin-layui`。
- 每个任务先建立失败测试，提交中不得包含 `.trae/`、`test-results*` 或根目录临时输出。

---

### Task 1: 为 093 建立真实双库恢复与运行时契约门禁

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration093DocumentAdminNetParityRecoveryTests.cs`
- Modify: `eng/testing/test-matrix.json`
- Modify: `contracts/naming/naming-debt.json`
- Modify: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/093_DocumentAdminNetParity.sql` only when RED test proves a mismatch
- Modify: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/093_DocumentAdminNetParity.sql` only when RED test proves a mismatch

**Interfaces:**
- Produces: tables whose columns exactly satisfy `DocumentPermissionSql` and `DocumentShareSql`; `migrationSelections["093"]`.

- [ ] **Step 1: 写 schema/partial recovery RED 测试**

SQL Server/MySQL 都必须断言：permission 列为 `Id, TenantId, DocumentId, UserId, PermissionLevel, CreatedAtUtc`；share 列为 `Id, TenantId, DocumentId, ShareCode, CreatedAtUtc, ExpireTime, Password, MaxAccessCount, AccessCount, IsEnabled, Version`。插入一行 permission 和 share 后删除非唯一索引或制造可恢复的缺列状态，删除 093 schema version，重跑并断言数据保留、结构收敛、再次运行执行数为 0。

- [ ] **Step 2: 注册选择器**

```json
"093": {
  "filter": "FullyQualifiedName~Migration093DocumentAdminNetParityRecoveryTests.MySql_|FullyQualifiedName~Migration093DocumentAdminNetParityRecoveryTests.SqlServer_"
}
```

将 naming debt 的 093 理由改成只陈述固定存储过程和固定 DDL 的事实；只有恢复测试真正通过后才允许写“恢复测试对齐”。

- [ ] **Step 3: 运行双库门禁**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Migration093DocumentAdminNetParityRecoveryTests"`

Run: `pnpm test:naming`

Expected: SQL Server/MySQL recovery PASS，命名门禁 24/24 或当时登记的完整集合 PASS。

- [ ] **Step 4: 提交**

```bash
git add src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/093_DocumentAdminNetParity.sql src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/093_DocumentAdminNetParity.sql tests/Full.NET.IntegrationTests/Migrations/Migration093DocumentAdminNetParityRecoveryTests.cs eng/testing/test-matrix.json contracts/naming/naming-debt.json
git commit -m "fix(document): align parity migration with persistence"
```

### Task 2: 实现安全的分享口令协议

**Files:**
- Create: `src/Modules/Full.NET.Modules.Document/Security/DocumentSharePasswordHasher.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/DocumentModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentShares/HostDocumentShareManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentShares/HostDocumentShareQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentShares/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Persistence/DocumentItemRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Persistence/DocumentShareSql.cs`
- Test: `tests/Full.NET.UnitTests/Document/DocumentShareSecurityTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Document/DocumentShareSecurityAssertions.cs`

**Interfaces:**
- Produces: `AccessHostDocumentShareRequest(string? Password)`; response JSON property `hasPassword: bool`; internal `PasswordHash` column/property only.
- Consumes: ASP.NET Core `PasswordHasher<DocumentSharePasswordSubject>` or an equivalent PBKDF2 implementation using repository-pinned framework packages.

- [ ] **Step 1: 写 RED 安全测试**

断言：创建带口令分享后数据库不包含明文；管理查询和匿名响应 JSON 均不包含 `password`/`passwordHash`；空口令访问返回 `document.host_share.password_required`；错误口令不增加 `AccessCount`；正确口令成功；相同口令两次创建得到不同 hash；旧构造调用仍可编译。

- [ ] **Step 2: 运行并确认 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DocumentShareSecurityTests"`

Expected: FAIL，当前实现对带口令创建失败关闭。

- [ ] **Step 3: 修改内部持久化命名**

把 SQL/record 的内部 `Password` 改为 `PasswordHash`，数据库列长度 SQL Server `nvarchar(1024)`、MySQL `varchar(1024)`。不要把 hash 放入任何 public response。`HostDocumentShareResponse` 增加 `bool HasPassword`，并保留一个带旧 `string? Password` 参数的兼容构造函数，兼容参数只能转换为 `HasPassword = !string.IsNullOrEmpty(Password)`，属性本身继续 `[JsonIgnore]` 且始终为 null。

- [ ] **Step 4: 实现哈希和验证**

```csharp
internal interface IDocumentSharePasswordHasher
{
    string Hash(Guid shareId, string password);
    bool Verify(Guid shareId, string passwordHash, string providedPassword);
}
```

校验创建口令长度 8–128 个 Unicode 字符。验证失败统一返回同一 ProblemDetails，不区分分享不存在与口令错误的耗时敏感细节；rehash-needed 时在受控写事务更新 hash。

- [ ] **Step 5: 把匿名访问改为 POST**

新增 `POST /api/v1/document/public/shares/{shareCode}/access` 接收 `AccessHostDocumentShareRequest`。旧 `GET /by-code/{shareCode}` 立即返回 405 或移除 Endpoint 映射；不要保留 GET 的计数副作用。共享 TypeScript API 同步改用 POST。

- [ ] **Step 6: 运行双库安全测试**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~DocumentShareSecurityAssertions"`

Expected: SQL Server/MySQL 全部 PASS，日志、ProblemDetails、响应和数据库抽样均无明文口令。

- [ ] **Step 7: 提交**

```bash
git add src/Modules/Full.NET.Modules.Document tests/Full.NET.UnitTests/Document tests/Full.NET.IntegrationTests/Document
git commit -m "feat(document): secure share password access"
```

### Task 3: 原子化匿名访问次数和状态判定

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Document/Persistence/DocumentShareSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentShares/HostDocumentShareManagementService.cs`
- Test: `tests/Full.NET.IntegrationTests/Document/DocumentShareConcurrencyAssertions.cs`

**Interfaces:**
- Produces: `TryConsumeAccess` SQL statement，成功只在 `IsEnabled = true`、`ExpireTime >= @Now`、`MaxAccessCount IS NULL OR AccessCount < MaxAccessCount` 且 `Version = @Version` 时把计数和版本各加一。

- [ ] **Step 1: 写并发 RED 测试**

对 `MaxAccessCount = 1` 的同一分享并发发起 20 个正确口令请求，SQL Server/MySQL 都必须恰好 1 个成功、19 个返回上限/冲突类业务错误，最终 `AccessCount = 1`。过期、禁用和错误口令均保持计数不变。

- [ ] **Step 2: 实现条件更新**

```sql
UPDATE fn_document_share
SET AccessCount = AccessCount + 1,
    Version = Version + 1
WHERE Id = @Id
  AND TenantId IS NULL
  AND IsEnabled = 1
  AND ExpireTime >= @Now
  AND (MaxAccessCount IS NULL OR AccessCount < MaxAccessCount)
  AND Version = @Version;
```

受影响行为 1 才返回成功；0 时重新读取并映射成 expired/disabled/max-access/version-conflict，不允许先自增再回滚应用层判断。

- [ ] **Step 3: 运行并提交**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~DocumentShareConcurrencyAssertions"`

Expected: 两种 Provider 各重复运行 10 轮无超卖。

```bash
git add src/Modules/Full.NET.Modules.Document tests/Full.NET.IntegrationTests/Document/DocumentShareConcurrencyAssertions.cs
git commit -m "fix(document): consume share access atomically"
```

### Task 4: 补齐 Document 后端纵向切片验证

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Document/DocumentAdminNetParityAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/ApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/ApiMySqlTests.cs`
- Modify: `eng/testing/test-matrix.json` if a Document-focused affected target does not exist

**Interfaces:**
- Consumes: categories、tags、items、versions、recycle-bin、permissions、shares、statistics endpoints.
- Produces: identical provider-neutral assertion helper invoked by SQL Server/MySQL fixtures.

- [ ] **Step 1: 写完整行为矩阵**

覆盖分页/筛选、乐观并发、软删除/恢复/永久删除、分类标签被引用保护、权限替换事务回滚、分享安全协议、统计口径、Host-only 租户失败关闭、每个操作的精确 permission deny/allow。断言 ProblemDetails 稳定机器码，不依赖翻译文本。

- [ ] **Step 2: 运行双库切片**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~DocumentAdminNetParityAssertions|FullyQualifiedName~DocumentAuthorizationAssertions"`

Expected: SQL Server/MySQL 断言集合完全相同且全部 PASS。

- [ ] **Step 3: 提交**

```bash
git add tests/Full.NET.IntegrationTests/Document tests/Full.NET.IntegrationTests/Api eng/testing/test-matrix.json
git commit -m "test(document): verify admin parity on both providers"
```

### Task 5: 补齐 Vue 页面、契约与无障碍验收

**Files:**
- Create: `ui/admin/src/views/DocumentRecycleBinView.vue`
- Create: `ui/admin/src/views/DocumentSharesView.vue`
- Create: `ui/admin/src/views/DocumentPermissionsView.vue`
- Create: `ui/admin/src/views/DocumentStatisticsView.vue`
- Create: matching `*.test.ts` files beside each view
- Delete: `ui/admin/src/api/document-categories.ts`
- Delete: `ui/admin/src/api/document-items.ts`
- Delete: `ui/admin/src/api/document-tags.ts`
- Modify: `ui/admin/src/router/index.ts`
- Modify: `ui/admin/src/api/document-shares.ts`
- Create: `contracts/openapi/document-host-parity-v1.json`
- Create: `tests/openapi/document-host-parity-contract.test.mjs`
- Modify: `contracts/openapi/vue-client-coverage-v1.json`
- Modify: `tests/openapi/vue-client-contract-coverage.test.mjs`
- Modify: `packages/client-contracts/src/index.ts`
- Modify: `packages/client-contracts/src/document-permissions.ts`
- Modify: `packages/client-contracts/src/document-shares.ts`
- Modify: `packages/client-contracts/src/document-recycle-bin.ts`
- Modify: `packages/client-contracts/src/document-statistics.ts`
- Modify: `tests/e2e/admin-parity/tests/accessibility-i18n.spec.mjs`

**Interfaces:**
- Consumes: Document OpenAPI endpoints and stable permission codes from `HostDocumentPermissions.cs`.
- Produces: routes `/document/recycle-bin`, `/document/shares`, `/document/permissions`, `/document/statistics` matching navigation catalog component keys.

- [ ] **Step 1: 写组件和权限 RED 测试**

每页断言 loading/empty/error/success、分页、危险操作确认、409 版本冲突刷新、zh-CN/en-US、无权限时不创建按钮或请求。分享页不得把口令回填到表格或编辑表单。

- [ ] **Step 2: 实现页面和严格契约守卫**

先删除未使用且与新版重复的 `ui/admin/src/api/document-categories.ts`、`document-items.ts`、`document-tags.ts`，让现有 `host-document-*` API 继续对应已冻结 v1 契约；不得同时保留两套同一路由客户端。为 permissions、recycle-bin、shares、statistics 创建 `document-host-parity-v1.json`，把四个生产 API 模块逐一登记到 coverage manifest，并把模块总数断言改成实际枚举值。共享契约字段必须逐项匹配 C#；当前 `document-permissions.ts` 的 `permissionType/objectType/objectId` 和 `document-shares.ts` 的 `documentNo/shareUrl/sharePermission` 与后端不一致，必须以 Task 2/4 验证后的 C# 契约为准统一，不能只添加 manifest 掩盖漂移。`HostDocumentShareResponse` 只接受 `hasPassword: boolean`，出现 `password` 或 `passwordHash` 时 runtime guard 必须失败。路由组件 key 与 `DocumentAuthorizationContributor` 完全一致；按钮分别使用 create/update-status/restore/purge/set-permissions 权限。

- [ ] **Step 3: 修复已有 WCAG 失败**

`tests/e2e/admin-parity/test-results.json` 已记录 `.art-tabs__title` 对比度 4.45、表头文字 2.94 和无 label combobox。修改实际 design token/组件 label，使 axe 结果为空；不要更新快照去接受违规。

- [ ] **Step 4: 运行前端门禁**

Run: `pnpm --filter @fullnet/admin test`

Run: `pnpm --filter @fullnet/admin typecheck`

Run: `pnpm --filter @fullnet/admin build`

Run: `pnpm test:e2e:admin-parity`

Expected: 单元、类型、构建和 Playwright 全部 exit 0，axe 0 violations。

- [ ] **Step 5: 提交**

```bash
git add ui/admin tests/e2e/admin-parity
git commit -m "feat(document): deliver parity administration views"
```

### Task 6: 合并候选验证与状态更新

**Files:**
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`
- Create: `docs/verification/document-parity-2026-08-09.md`

**Interfaces:**
- Consumes: Task 1–5 新鲜输出。
- Produces: 不超过证据的 capability 状态和命令记录。

- [ ] **Step 1: 运行完整相关门禁**

Run: `pnpm test:governance`

Run: `pnpm test:naming`

Run: `pnpm test:openapi`

Run: `dotnet build Full.NET.slnx -c Release --no-restore`

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-build`

Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-build`

Run: `pnpm test:integration:affected -- --base HEAD~5 --phase merge`

Run: `git diff --check`

Expected: 全部 exit 0；测试数量只更新 `eng/testing/test-matrix.json`。

- [ ] **Step 2: 写验证记录**

记录提交、Provider、确切命令与结果、分享安全与并发证据、Vue/E2E/WCAG 结果，以及未执行的生产容量项。只有后端双库与 Vue 真实 API 浏览器链路都通过时，Document 才保持 `Build-verified`。

- [ ] **Step 3: 提交**

```bash
git add docs/roadmap docs/verification/document-parity-2026-08-09.md eng/testing/test-matrix.json
git commit -m "docs(document): record parity verification"
```
