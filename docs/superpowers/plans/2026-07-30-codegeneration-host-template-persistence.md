# CodeGeneration Host Template Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task in the current shared workspace. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Host 管理端增加可持久化的代码生成模板目录，让 Vue/Layui 可以保存、分页读取、更新、软删除并重新预览 legacy 或显式能力 Schema，同时保持远程 Apply 与生成任务记录关闭。

**Architecture:** 先把现有 Preview 请求升级为兼容 legacy `hasVersion` 与显式 `entityCapabilities/scene/relationships` 的统一 Schema 契约，由一个无数据库依赖的归一化器完成严格机器码解析、领域不变量校验与规范 JSON 生成。模板目录使用 CodeGeneration 模块内的 Host-only Dapper 垂直切片、单往返分页、UUID v7、可信用户审计、Version 乐观并发和软删除；双管理端复用同一客户端契约与现有预览工作台。

**Tech Stack:** .NET 10 Minimal API、Dapper abstractions、System.Text.Json source generation、DbUp、SQL Server/MySQL、Vue 3/Element Plus、Layui ES modules、Vitest/MSTest/Playwright。

## Global Constraints

- 本计划内联执行，不创建 worktree、不委派子代理、不提交或暂存共享工作区文件。
- 实施前重新检查迁移目录；`038–043` 已分别预留给 GridPreference、SerialNumbers、JobsSchedules、IdentityRoleFieldGrant、IdentitySignatureNonce、AuditingOutboundCall，禁止占用。当前候选为双库 `044_CodeGenerationTemplate.sql`；若届时 `044` 已占用，先协调并顺延。
- `fn_codegeneration_template` 与全部 SQL 使用 `SqlDataScope.HostOnly`；不得写入 Global SQL catalog，不得接受 Tenant 上下文。
- legacy 请求继续接受精确 PascalCase 机器码；新保存的规范 Schema 一律输出稳定小写点分机器码。拒绝近似大小写、数字枚举、未知字段，以及同时提供非空 `hasVersion` 与 `entityCapabilities` 的输入；JSON `null` 分支按未提供处理且在规范输出中省略。
- 模板列表的 `COUNT + page` 必须通过 `IMultiResultQueryExecutor` 在一次数据库往返和一个 Reader 生命周期内完成，最大 `pageSize = 100`，稳定排序为 `UpdatedAtUtc DESC, CreatedAtUtc DESC, Id`。
- 不增加缓存、Outbox、远程 Apply、仓库写入、生成任务记录或自动迁移执行；这些能力必须在模板版本、审计、环境门禁和执行记录具备后另立切片。
- 所有写操作从认证主体的 `sub` 解析 `Guid`，不得从请求体接受 Actor/UserId；创建、更新、删除使用 `IClock` 与 `IIdGenerator`。
- 数据库与公共契约变更必须 SQL Server/MySQL 双提供程序验证；只有最终 fresh discovery 可以更新 `eng/testing/test-matrix.json`。

---

### Task 1: 统一 Preview 与模板的 Schema 契约

**Files:**

- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Contracts/CodeGenerationPreviewContracts.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Features/NormalizeCrudSchema/CodeGenerationSchemaNormalizer.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Features/PreviewCrudGeneration/CodeGenerationPreviewService.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Serialization/CodeGenerationJsonSerializerContext.cs`
- Modify: `packages/client-contracts/src/code-generation-previews.ts`
- Modify: `packages/client-contracts/tests/code-generation-previews.test.ts`
- Modify: `contracts/openapi/code-generation-previews-v1.json`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationPreviewServiceTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Api/OpenApiCodeGenerationPreviewsContractAssertions.cs`

**Interfaces:**

- `CodeGenerationPreviewRequest.HasVersion` becomes `bool?`; existing C# constructors and existing JSON `hasVersion: true|false` remain valid. New nullable branch properties use `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` so canonical JSON contains exactly one capability shape.
- Add `CodeGenerationEntityCapabilitiesRequest`, `CodeGenerationRelationshipRequest`, optional `EntityCapabilities`, `Scene`, and `Relationships`.
- Add `CodeGenerationSchemaNormalizer.Normalize(CodeGenerationPreviewRequest)` returning `Result<NormalizedCodeGenerationSchema>`.
- `NormalizedCodeGenerationSchema` exposes `FullNetCrudSchema Schema`, `CodeGenerationPreviewRequest CanonicalRequest`, `string CanonicalJson`, and lowercase 64-character `SchemaSha256`.

- [x] **Step 1: Write failing compatibility and explicit-capability tests**

  Add tests proving:

  ```csharp
  var legacy = CreateRequest() with { HasVersion = true };
  Assert.IsTrue(normalizer.Normalize(legacy).IsSuccess);

  var explicitRequest = CreateRequest() with
  {
      HasVersion = null,
      EntityCapabilities = new(
          "soft.delete",
          HasCreatedAudit: true,
          HasUpdatedAudit: true,
          HasDeletedAudit: true,
          HasVersion: true,
          "none"),
      Scene = "single",
      Relationships = [],
  };
  var normalized = normalizer.Normalize(explicitRequest);
  Assert.IsTrue(normalized.IsSuccess);
  StringAssert.Contains(normalized.Value!.CanonicalJson, "\"deleteMode\":\"soft.delete\"");
  ```

  Also assert stable failure `codegen.preview.invalid_schema` for both-shapes, neither-shape, numeric/unknown machine codes, `relationships: null`, more than 128 columns, and domain-invalid capability columns.

- [x] **Step 2: Run the focused RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~CodeGenerationPreviewServiceTests"
  pnpm --filter @fullnet/client-contracts test -- code-generation-previews
  ```

  Expected: compile/test failure because the explicit DTOs and normalizer do not yet exist.

- [x] **Step 3: Implement the shared normalizer and contract union**

  Use this invariant:

  ```csharp
  var usesLegacy = request.HasVersion is not null;
  var usesExplicit = request.EntityCapabilities is not null;
  if (usesLegacy == usesExplicit
      || (usesLegacy && (request.Scene is not null || request.Relationships is not null))
      || (usesExplicit && request.Relationships is null))
  {
      return Result<NormalizedCodeGenerationSchema>.Failure(InvalidSchema);
  }
  ```

  Parse only the stable values and exact compatibility aliases already defined by `FullNetCrudWireValues`; because that type is internal to the generator assembly, keep the mapping local to the module and cover every accepted value with tests. Build `FullNetCrudSchema` through its public overloads, catch only input-related `ArgumentException`, serialize the canonical request with source-generated System.Text.Json metadata while omitting nullable inactive-branch properties, then hash the UTF-8 bytes with SHA-256. Client `dataScope` and column `scalarType` unions/validators must accept both exact legacy aliases and stable lowercase wire values, while server canonical responses always choose lowercase values.

  In TypeScript model the request as a discriminated union:

  ```ts
  export type CodeGenerationPreviewRequest =
    CodeGenerationSchemaBase & (
      | {
          hasVersion: boolean;
          entityCapabilities?: never;
          scene?: never;
          relationships?: never;
        }
      | {
          hasVersion?: never;
          entityCapabilities: CodeGenerationEntityCapabilitiesRequest;
          scene: CodeGenerationScene;
          relationships: CodeGenerationRelationshipRequest[];
        }
    );
  ```

- [x] **Step 4: Reuse the normalizer from Preview and verify GREEN**

  `CodeGenerationPreviewService` must generate artifacts from `normalized.Schema` and retain its cancellation/secure-error behavior. Update JSON source generation and OpenAPI property assertions for all nested DTOs.

  Run the same two focused commands; expected: all focused tests pass and legacy fixtures require no payload rewrite.

---

### Task 2: Host-only template persistence and migration

**Files:**

- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Full.NET.Modules.CodeGeneration.csproj`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Contracts/CodeGenerationTemplateContracts.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Persistence/CodeGenerationTemplateRecord.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Persistence/CodeGenerationTemplateSql.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostTemplates/CodeGenerationTemplateQueryService.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostTemplates/CodeGenerationTemplateManagementService.cs`
- Create after migration-number recheck: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/044_CodeGenerationTemplate.sql`
- Create after migration-number recheck: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/044_CodeGenerationTemplate.sql`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationTemplateServiceTests.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationTemplateSqlTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Migrations/Migration044CodeGenerationTemplateRecoveryTests.cs`

**Interfaces:**

  ```csharp
  public sealed record CreateCodeGenerationTemplateRequest(
      string Name,
      string? Description,
      CodeGenerationPreviewRequest Schema);

  public sealed record UpdateCodeGenerationTemplateRequest(
      string Name,
      string? Description,
      CodeGenerationPreviewRequest Schema,
      long Version);

  public sealed record DeleteCodeGenerationTemplateRequest(long Version);

  public sealed record CodeGenerationTemplateResponse(
      Guid Id,
      string Name,
      string? Description,
      CodeGenerationPreviewRequest Schema,
      string SchemaSha256,
      DateTimeOffset CreatedAtUtc,
      Guid CreatedByUserId,
      DateTimeOffset? UpdatedAtUtc,
      Guid? UpdatedByUserId,
      long Version);
  ```

- [x] **Step 1: Write persistence RED tests**

  Use fakes for `IMultiResultQueryExecutor`, `IQueryExecutor`, `ICommandExecutor`, `ICommandTransaction`, `IClock`, and `IIdGenerator`. Assert:

  - list clamps page/pageSize and invokes exactly one multi-result statement;
  - create trims `Name`/`Description`, stores only `CanonicalJson` and its hash, starts at `Version = 1`;
  - update requires matching Version and changes trusted update audit fields;
  - delete is soft delete and distinguishes not-found from version conflict;
  - all SQL statements are `HostOnly`, parameterized, filter `IsDeleted = 0`, and never use `SELECT *`.

- [x] **Step 2: Run the focused RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~CodeGenerationTemplate"
  ```

  Expected: compile failure because template contracts/services/SQL do not exist.

- [x] **Step 3: Implement the vertical persistence slice**

  Add the `Full.NET.Data.Abstractions` project reference. Keep records and SQL private/internal to CodeGeneration. The page statement must contain two ordered result sets:

  ```sql
  SELECT COUNT(1)
  FROM fn_codegeneration_template
  WHERE IsDeleted = 0;

  SELECT Id, Name, Description, SchemaJson, SchemaSha256,
         CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, Version
  FROM fn_codegeneration_template
  WHERE IsDeleted = 0
  ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC, Id
  -- SQL Server: OFFSET/FETCH
  -- MySQL: LIMIT/OFFSET
  ```

  Update/delete SQL must include `AND Version = @Version AND IsDeleted = 0`, increment Version once, and never overwrite create audit fields. Deserialize stored JSON only through the strict source-generated contract; corrupt stored JSON must fail loudly as an internal data-integrity error, not be returned as caller validation.

- [x] **Step 4: Add paired, restart-safe migration 044**

  After confirming `044` is still free, create `fn_codegeneration_template` with:

  - UUID v7 `Id` (`uniqueidentifier` / `BINARY(16)`);
  - `Name` length 128, nullable `Description` length 512;
  - provider-appropriate unbounded `SchemaJson`, fixed lowercase SHA-256 column;
  - trusted create/update/delete audit columns, `IsDeleted`, `Version`;
  - stable active-list index beginning with `IsDeleted` and matching the list ordering.

  Recovery tests must create a half-complete state, remove the DbUp journal row, rerun migration, and verify both SQL Server and MySQL converge without dropping data.

- [x] **Step 5: Verify Unit and migration selection**

  Run the focused Unit command; expected: all template Unit tests pass. Run the exact `Migration044CodeGenerationTemplateRecoveryTests` only after the shared Docker queue is released; expected: both providers pass and Testcontainers/Ryuk fully tear down.

---

### Task 3: HTTP, authorization, source generation, and dual-provider API

**Files:**

- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostTemplates/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Contracts/CodeGenerationTemplateContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationModule.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Serialization/CodeGenerationJsonSerializerContext.cs`
- Create: `contracts/openapi/code-generation-templates-v1.json`
- Create: `tests/Full.NET.IntegrationTests/Api/OpenApiCodeGenerationTemplatesContractAssertions.cs`
- Create: `tests/Full.NET.IntegrationTests/CodeGeneration/CodeGenerationTemplateAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/CodeGenerationApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/CodeGenerationApiMySqlTests.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationPreviewServiceTests.cs`

**Interfaces:**

- Permissions: `codegen.templates.read`, `codegen.templates.write`, both `AuthorizationScope.Host`.
- Routes:
  - `GET /api/v1/code-generation/templates?page=1&pageSize=20`
  - `GET /api/v1/code-generation/templates/{templateId}`
  - `POST /api/v1/code-generation/templates`
  - `PUT /api/v1/code-generation/templates/{templateId}`
  - `POST /api/v1/code-generation/templates/{templateId}/delete`
- Stable errors: `codegen.template.invalid`, `codegen.template.not_found`, `codegen.template.version_conflict`.

- [x] **Step 1: Write failing authorization/OpenAPI/API tests**

  Cover anonymous 401, missing permission 403, read/write permission separation, Host success, tenant-context denial, create/list/get/update/delete, hidden soft-deleted records, stale Version 409, not-found 404, invalid schema 400, and trustworthy audit user IDs.

- [x] **Step 2: Run focused RED when the shared .NET queue is released**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~CodeGenerationPreviewServiceTests|FullyQualifiedName~CodeGenerationTemplate"
  ```

  Expected: authorization catalog and endpoint-registration assertions fail before implementation.

- [x] **Step 3: Map and register the endpoints**

  Register query/management/normalizer services in `CodeGenerationModule`, add JSON metadata for every template/paged/nested DTO, and map endpoints with standard ProblemDetails. For writes:

  ```csharp
  var subject = httpContext.User.FindFirst("sub")?.Value;
  if (!Guid.TryParse(subject, out var actorUserId))
  {
      return Results.Unauthorized();
  }
  ```

  Keep the existing navigation entry; add read/write permissions without creating another menu item.

- [x] **Step 4: Run dual-provider focused API GREEN**

  After exclusive Docker handoff, run only CodeGeneration SQL Server/MySQL API tests plus migration 044 recovery. Expected: both providers pass, no tenant leakage, no residual SQL Server/MySQL/Ryuk containers.

---

### Task 4: Shared client contracts and Vue/Layui workbench

**Files:**

- Create: `packages/client-contracts/src/code-generation-templates.ts`
- Create: `packages/client-contracts/tests/code-generation-templates.test.ts`
- Modify only after Admin Task 3 releases it: `packages/client-contracts/src/index.ts`
- Create: `ui/admin/src/api/code-generation-templates.ts`
- Create: `ui/admin/src/api/code-generation-templates.test.ts`
- Modify: `ui/admin/src/views/CodeGenerationPreviewsView.vue`
- Modify: `ui/admin/src/views/CodeGenerationPreviewsView.test.ts`
- Create: `ui/admin-layui/js/core/code-generation-templates.js`
- Create: `ui/admin-layui/tests/code-generation-templates.test.js`
- Modify: `ui/admin-layui/js/core/code-generation-previews.js`
- Modify: `ui/admin-layui/index.html`

**Interfaces:**

- Both clients expose list/get/create/update/delete methods and validate all API responses through `@fullnet/client-contracts`.
- The existing schema editor remains the single source of preview input; loading a template replaces the editor JSON with the returned canonical Schema.
- UI actions are gated independently by `codegen.templates.read` and `codegen.templates.write`; Preview remains gated by `codegen.previews.read`.

- [x] **Step 1: Write failing contract/API/view tests**

  Assert exact stable machine codes, response shapes, page metadata, stale-version error rendering, no duplicate submit while loading, and permission-trimmed Save/Update/Delete controls in both clients.

- [x] **Step 2: Run client RED**

  Run:

  ```powershell
  pnpm --filter @fullnet/client-contracts test -- code-generation
  pnpm --filter @fullnet/admin test -- CodeGenerationPreviewsView
  pnpm --dir ui/admin-layui test -- code-generation
  ```

  Expected: missing template contract/API/controller failures.

- [x] **Step 3: Implement the minimum template catalog UI**

  Add a compact template list beside the existing Schema editor with Load, Save New, Update, and Delete. Preserve draft text when list loading fails; after successful writes, replace local Version/Schema with the server response. Display stable ProblemDetails code and translated title; never branch on translated prose.

- [x] **Step 4: Run full scoped client verification**

  Run client-contracts tests/build, full Vue tests/build, and full Layui tests/build. Expected: all pass with no type or ESM syntax errors.

---

### Task 5: Real-stack acceptance and lean documentation closeout

**Files:**

- Create: `tests/e2e/admin-real-stack/tests/host-code-generation-templates.spec.mjs`
- Modify only after all gates pass: `docs/roadmap/client-delivery-roadmap.md`
- Modify only from fresh discovery: `eng/testing/test-matrix.json`
- Modify: `tests/testing/test-matrix.test.mjs` only if migration selection `044` is added to the matrix contract.
- Modify this plan: mark completed checkboxes and append exact verification evidence.

**Interfaces:**

- Real-stack scenario runs once for Vue and once for Layui against the real API/database.
- It proves create → list → load → preview → update with fresh Version → stale update 409 → soft delete → absent from list.

- [x] **Step 1: Write and discover the real-stack E2E**

  Use Host administrator login and a restricted Host viewer. Assert read/write permission trimming, direct API 403, canonical explicit Schema after reload, preview artifact content, and persistence across page reload. Do not mock auth, navigation, API, or database.

- [x] **Step 2: Run affected planning before expensive tests**

  Run:

  ```powershell
  pnpm test:integration:affected:plan -- --snapshot codegeneration-host-templates-20260730 --phase inner
  ```

  Execute only the selector output during inner/slice development; do not run the complete local Integration suite.

- [x] **Step 3: Run the coordinated slice gates**

  After exclusive shared .NET/Docker handoff:

  - CodeGeneration focused Unit and Architecture tests;
  - migration 044 recovery on both providers;
  - affected slice from the task snapshot;
  - Vue/Layui real-stack template E2E;
  - full scoped client tests/builds.

  Confirm `docker ps` and relevant stopped-container inspection both report zero SQL Server/MySQL/Redis/Ryuk residuals.

- [x] **Step 4: Update only truthful status and fresh test counts**

  Advance only the template persistence sub-capability after backend, both clients, permissions, dual-provider migration/API, and E2E are green. Keep remote Apply, generated task/history records, rollback, and broader CodeGeneration capability open. Update `eng/testing/test-matrix.json` once from fresh Release discovery after all concurrent windows freeze.

- [x] **Step 5: Final verification**

  Run:

  ```powershell
  git diff --check
  git status --short
  git branch --show-current
  ```

  Report exact commands/results, snapshot affected scope, deferred gates, and one-line Rule/Skill evolution result. Do not describe an unexecuted gate as passed.

## Verification Evidence

- 任务快照：`codegeneration-host-templates-20260730`；迁移使用已协调的双库 `044_CodeGenerationTemplate.sql`，物理表为模块所有权一致的 `fn_codegeneration_template`。
- `dotnet build Full.NET.slnx -c Release --no-restore`：0 warning / 0 error。
- `pnpm test:dotnet:unit -- --no-build`：fresh discovery **805/805**；`pnpm test:dotnet:architecture -- --no-build`：**49/49**。
- CodeGeneration Unit：**213/213**；模板查询/管理/SQL 审查后聚焦：**9/9**。持久化读路径会重新规范化 Schema 并核对 SHA-256，损坏或篡改数据按内部完整性错误失败。
- `pnpm test:naming`：**23/23**；`pnpm test:sql-safety`：**5/5**。044 存储过程与条件 DROP 仅登记两条精确、限期的扫描器语法债务。
- client-contracts：**99/99** + build；Vue：**246/246** + build；Layui：**121/121** + build。模板读写权限分别控制目录和写入表单。
- `pnpm test:integration:affected --snapshot codegeneration-host-templates-20260730 --phase slice`：Release Integration build 0/0、治理 **16/16**、smoke **8/8**、CodeGeneration/Files/migration-038/migration-044/Realtime/Settings 双库影响集 **45/45**。
- 双管理端真实栈 `host-code-generation-templates.spec.mjs`：**4/4**，覆盖持久化重载、预览、更新、陈旧版本 409、软删除 404、受限用户 API 403 与导航裁剪。
- 测试矩阵契约：**4/4**；Integration fresh discovery：full **239**、API SQL Server **41**、API MySQL **41**、migrations **74**、infrastructure **83**，分片无遗漏或重复。
- 最终 Docker 检查：运行中 SQL Server/MySQL/Redis/Ryuk **0**，相关停止残留 **0**。
- 独立代码审查：无 Critical；已修复持久化完整性校验、读写权限独立、成功删除审计断言和表所有权问题。`Unspecified` 继续作为内部哨兵并在 Host 入口 fail-closed，符合显式数据作用域约束。
- 本地完整 Integration 矩阵未执行；按仓库测试策略保留给 `main` CI 的互斥并行分片门禁。
