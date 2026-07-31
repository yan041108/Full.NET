# CodeGeneration Run History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Host 代码生成工作台增加可审计、可分页查询的受跟踪预览运行记录，为后续管理端 Apply 与回滚建立可信执行前置。

**Architecture:** 保留现有 `/api/v1/code-generation/previews` 纯内存兼容入口，新增 `/api/v1/code-generation/runs/preview` 受跟踪入口和只读运行目录。受跟踪入口支持“内联 Schema”或“已保存模板＋版本”二选一，在返回成功前持久化一次不可变摘要；数据库只保存身份、模板版本、规范 Schema/Manifest 摘要、状态、稳定错误码和时间，不保存生成源码、原始请求或异常文本。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper、DbUp、SQL Server、MySQL、System.Text.Json source generation、Vue 3/Element Plus、Layui、Vitest、Node Test Runner、Playwright。

## Global Constraints

- 仅限 Host 作用域；所有 SQL 使用 `SqlDataScope.HostOnly`，不登记 `global-sql-statements.json`。
- 实施前重新检查迁移目录；`040–043` 继续保留给 Admin.NET 后续任务，`044` 已属于 CodeGenerationTemplate。本计划候选为双库 `045_CodeGenerationRun.sql`，若被占用必须先协调后顺延。
- 新增权限固定为 `codegen.runs.read` 与 `codegen.runs.execute`；读写权限相互独立，默认超级管理员通过授权目录动态获得。
- 运行状态机器码固定为 `succeeded`、`failed`；操作机器码本切片只允许 `preview`。
- 失败记录只保存稳定错误码；不得保存异常消息、堆栈、Schema JSON、生成源码、绝对路径或仓库内容。
- 取消令牌必须继续传播；请求在生成前取消时不得创建伪失败记录。
- 受跟踪预览只有在记录成功写入后才返回成功；数据库写入失败不得向客户端返回“未记录的成功”。
- 不增加 Worker、队列、缓存、Outbox、远程 Apply、仓库写入或回滚执行。
- 双库表主键使用应用端 UUID v7；SQL Server 使用 `uniqueidentifier`，MySQL 使用 `BINARY(16)`。
- 本地只运行任务快照选中的 affected Integration；完整测试集合留给主分支 CI。

---

### Task 1: 不可变运行记录与双库迁移

**Files:**
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Contracts/CodeGenerationRunContracts.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Persistence/CodeGenerationRunRecord.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Persistence/CodeGenerationRunSql.cs`
- Create after migration-number recheck: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/045_CodeGenerationRun.sql`
- Create after migration-number recheck: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/045_CodeGenerationRun.sql`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationRunSqlTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration045CodeGenerationRunRecoveryTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/SqlServerMigrationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/MySqlMigrationTests.cs`

**Interfaces:**
- Produces `CodeGenerationRunPermissions.Read/Execute`, `CodeGenerationRunOperationKinds.Preview`, `CodeGenerationRunStatuses.Succeeded/Failed`.
- Produces `CodeGenerationRunPreviewRequest(Guid? TemplateId, long? TemplateVersion, CodeGenerationPreviewRequest? Schema)`.
- Produces `CodeGenerationRunResponse` and `CodeGenerationRunPreviewResponse(Guid RunId, CodeGenerationPreviewResponse Preview)`.
- Produces `CodeGenerationRunSql.Insert`, `FindById`, `PageSqlServer`, and `PageMySql`.

- [x] **Step 1: Write the failing SQL contract tests**

  Assert that both page statements use one batch containing `COUNT(1)` and the ordered page, are `HostOnly`, clamp through service parameters, and never select a content/JSON/error-message column. Assert that `Insert` contains only the immutable columns below.

  ```csharp
  Assert.AreEqual(SqlDataScope.HostOnly, CodeGenerationRunSql.Insert.Scope);
  StringAssert.Contains(CodeGenerationRunSql.Insert.Text, "SchemaSha256");
  Assert.IsFalse(CodeGenerationRunSql.Insert.Text.Contains("SchemaJson"));
  Assert.IsFalse(CodeGenerationRunSql.Insert.Text.Contains("ErrorMessage"));
  ```

- [x] **Step 2: Run the focused test and verify RED**

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~CodeGenerationRunSqlTests"
  ```

  Expected: compile failure because `CodeGenerationRunSql` and run contracts do not exist.

- [x] **Step 3: Add the minimal contracts, record, and SQL**

  Use this response shape:

  ```csharp
  public sealed record CodeGenerationRunResponse(
      Guid Id,
      Guid? TemplateId,
      long? TemplateVersion,
      string OperationKind,
      string Status,
      string? ModuleKey,
      string? EntityKey,
      string? SchemaSha256,
      int ArtifactCount,
      string? ManifestSha256,
      string? ErrorCode,
      Guid RequestedByUserId,
      DateTimeOffset StartedAtUtc,
      DateTimeOffset FinishedAtUtc);
  ```

  `PageSqlServer` and `PageMySql` must return total plus rows in one round trip, filter only by optional `@Status`, and order by `StartedAtUtc DESC, Id`. The insert statement must not contain an update path.

- [x] **Step 4: Add paired idempotent migration 045**

  Create `fn_codegeneration_run` with the response fields above. Add checks enforcing:

  - `OperationKind = 'preview'`;
  - `Status IN ('succeeded', 'failed')`;
  - template id/version are both null or both non-null and version is positive;
  - success requires lowercase 64-character Schema/Manifest hashes, `ArtifactCount > 0`, and null `ErrorCode`;
  - failure requires `ArtifactCount = 0`, null `ManifestSha256`, and non-empty `ErrorCode`;
  - `FinishedAtUtc >= StartedAtUtc`.

  Add the exact page index `(Status, StartedAtUtc DESC, Id)` and repair an absent or wrong-shaped index on rerun, following migration 044’s recovery pattern.

- [x] **Step 5: Add and run dual-provider recovery tests**

  Recovery tests must cover clean creation, missing-index repair, wrong-index repair, and idempotent rerun. Run them only after the shared Docker owner explicitly releases:

  ```powershell
  dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~Migration045CodeGenerationRunRecoveryTests"
  ```

  Expected: SQL Server and MySQL recovery cases pass; Docker/Ryuk teardown leaves no residual containers.

### Task 2: 受跟踪预览编排与运行查询

**Files:**
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationRunService.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationRunQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Features/PreviewCrudGeneration/CodeGenerationPreviewService.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationRunServiceTests.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationRunQueryServiceTests.cs`

**Interfaces:**
- Consumes `CodeGenerationSchemaNormalizer`, `CodeGenerationPreviewService`, template query service, `ICommandExecutor`, `IMultiResultQueryExecutor`, `IClock`, and `IIdGenerator`.
- Produces `PreviewAsync(Guid actorUserId, CodeGenerationRunPreviewRequest request, CancellationToken)` and paged/get query methods.

- [x] **Step 1: Write failing service tests**

  Cover these exact behaviors:

  - exactly one of inline `Schema` or `TemplateId + TemplateVersion` is required;
  - template mode rejects missing templates and stale versions without generating;
  - success computes a deterministic manifest SHA-256 from sorted `path + "\n" + kind + "\n" + sha256 + "\n"` entries;
  - success inserts one immutable record before returning the preview;
  - validation failure inserts one record with only stable `ErrorCode`;
  - cancellation before generation inserts no record;
  - persistence failure propagates and never returns preview success.

- [x] **Step 2: Run the focused tests and verify RED**

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~CodeGenerationRun"
  ```

  Expected: compile failure for missing services.

- [x] **Step 3: Implement the minimal orchestration**

  Keep the existing pure `Preview` method unchanged. Add an internal normalized preview method only if needed to avoid normalizing twice; do not make the compatibility endpoint database-dependent. Use `IClock.UtcNow` for start/finish, `IIdGenerator.NewId()` for the run id, and do not catch `OperationCanceledException`.

  For unexpected generator failures, persist `codegen.run.generation_failed` and return an `ErrorType.Failure` without exception text. For known validation/template failures, persist their existing stable error code. If the failure record itself cannot be written, propagate the database exception.

- [x] **Step 4: Implement single-round-trip page queries**

  Clamp page to at least 1 and page size to `1..100`. Accept optional status only when null, `succeeded`, or `failed`; otherwise return `codegen.run.invalid_query`. Verify record mapping preserves nullable template and failure fields.

- [x] **Step 5: Run focused Unit tests GREEN**

  Run the command from Step 2. Expected: every `CodeGenerationRun*` Unit test passes.

### Task 3: Host API、权限与双库真实栈

**Files:**
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationModule.cs`
- Modify: `src/Modules/Full.NET.Modules.CodeGeneration/Serialization/CodeGenerationJsonSerializerContext.cs`
- Create: `contracts/openapi/code-generation-runs-v1.json`
- Create: `tests/Full.NET.IntegrationTests/Api/OpenApiCodeGenerationRunsContractAssertions.cs`
- Create: `tests/Full.NET.IntegrationTests/CodeGeneration/CodeGenerationRunAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/CodeGenerationApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/CodeGenerationApiMySqlTests.cs`

**Interfaces:**
- Maps `POST /api/v1/code-generation/runs/preview`, `GET /api/v1/code-generation/runs`, and `GET /api/v1/code-generation/runs/{runId:guid}`.
- POST requires `codegen.runs.execute`; GET endpoints require `codegen.runs.read`.

- [x] **Step 1: Write failing authorization/OpenAPI/API tests**

  Assert independent read/execute permissions, Host-only access, verified `sub` actor resolution, standard ProblemDetails, page limits, template-version enforcement, absence of source content in history JSON, and one persisted row for successful and invalid tracked previews.

- [x] **Step 2: Run focused Unit/API discovery and verify RED**

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~CodeGenerationRun"
  ```

  Expected: missing endpoint/authorization registration assertions fail.

- [x] **Step 3: Implement endpoint and registration**

  Resolve actor id only from the verified `sub` claim. Register run services as scoped, add all run DTOs and `PagedResult<CodeGenerationRunResponse>` to the JSON source context, and map endpoints in `CodeGenerationModule`.

- [x] **Step 4: Run dual-provider focused API tests**

  After exclusive Docker handoff:

  ```powershell
  dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~CodeGenerationApiSqlServerTests|FullyQualifiedName~CodeGenerationApiMySqlTests|FullyQualifiedName~Migration045CodeGenerationRunRecoveryTests"
  ```

  Expected: both providers pass with identical contracts and no residual containers.

### Task 4: 共享客户端契约与双管理端运行历史

**Files:**
- Create: `packages/client-contracts/src/code-generation-runs.ts`
- Create: `packages/client-contracts/tests/code-generation-runs.test.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Create: `ui/admin/src/api/code-generation-runs.ts`
- Create: `ui/admin/src/api/code-generation-runs.test.ts`
- Modify: `ui/admin/src/views/CodeGenerationPreviewsView.vue`
- Modify: `ui/admin/src/views/CodeGenerationPreviewsView.test.ts`
- Create: `ui/admin-layui/js/core/code-generation-runs.js`
- Modify: `ui/admin-layui/js/core/code-generation-previews.js`
- Modify: `ui/admin-layui/index.html`
- Create: `ui/admin-layui/tests/code-generation-runs.test.js`
- Modify: `ui/admin-layui/tests/code-generation-preview-controller.test.js`
- Modify: `packages/admin-i18n/src/messages.ts`

**Interfaces:**
- Produces strict runtime guards for run request, response, preview wrapper, and page.
- Both clients submit to the tracked endpoint and render the newest 20 run summaries when `codegen.runs.read` is present.

- [x] **Step 1: Write failing contract and adapter tests**

  Reject unknown operation/status values, malformed hashes, negative counts, inconsistent template pairs, source-content fields in history rows, and invalid pages. Assert execute-only users can preview without reading history and read-only users cannot trigger preview.

- [x] **Step 2: Run client tests and verify RED**

  ```powershell
  pnpm --filter @fullnet/client-contracts test -- code-generation-runs
  pnpm --filter @fullnet/admin test -- CodeGenerationPreviewsView code-generation-runs
  pnpm --filter @fullnet/admin-layui test -- code-generation-runs code-generation-preview-controller
  ```

- [x] **Step 3: Implement strict contracts and adapters**

  The tracked request is the exact union:

  ```ts
  export type CodeGenerationRunPreviewRequest =
    | { templateId: string; templateVersion: number; schema?: never }
    | { templateId?: never; templateVersion?: never; schema: CodeGenerationPreviewRequest };
  ```

  The Vue and Layui adapters validate every response before returning it.

- [x] **Step 4: Add compact history panels**

  Reuse the existing preview route; do not add a second navigation item. Render operation, status, entity key, artifact count, short hashes, actor, and start/finish timestamps as text nodes. Never render source content or error details in the history panel. After a successful tracked preview, refresh the first page.

- [x] **Step 5: Run all focused client tests GREEN**

  Run the three commands from Step 2. Expected: contracts, Vue, and Layui focused suites all pass.

### Task 5: 真实栈 E2E、路线图与切片收口

**Files:**
- Create: `tests/e2e/admin-real-stack/tests/host-code-generation-runs.spec.mjs`
- Modify: `tests/e2e/admin-real-stack/scripts/spec-contracts.test.mjs`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Append completion evidence: `docs/superpowers/plans/2026-07-31-codegeneration-run-history.md`
- Modify only after fresh discovery and all concurrent writers freeze: `eng/testing/test-matrix.json`

**Interfaces:**
- Produces real-stack proof for execute/read permission separation, inline and template tracked previews, immutable history, and both admin clients.

- [x] **Step 1: Write failing E2E contract**

  The E2E must create or select a template, run a tracked preview, verify the returned run appears in history after reload, verify source content is absent from the history payload, and verify read/execute permission separation. Clean up only created templates; run records are immutable evidence and are not deleted.

- [x] **Step 2: Run the focused E2E after exclusive stack handoff**

  ```powershell
  pnpm --dir tests/e2e/admin-real-stack test -- host-code-generation-runs
  ```

  Expected: RED before adapters/UI are complete, then GREEN for Vue and Layui paths.

- [x] **Step 3: Run affected slice verification**

  Using the task snapshot created immediately before Task 1 implementation:

  ```powershell
  pnpm test:integration:affected:plan -- --snapshot codegeneration-run-history-20260731 --phase inner
  pnpm test:integration:affected -- --snapshot codegeneration-run-history-20260731 --phase slice
  ```

  Expected: selector contains CodeGeneration plus migration 045 and any true shared-client consumers; build, governance, smoke, and selected dual-provider tests pass. Confirm Docker/Ryuk running and residual counts are zero.

- [x] **Step 4: Update only truthful roadmap state**

  Mark “生成任务记录” complete only after backend, both clients, migration recovery, dual-provider API, and real-stack E2E are green. Keep management Apply, rollback, Worker execution, remote repository writes, and production rollout explicitly open.

- [x] **Step 5: Final verification**

  Run focused CodeGeneration Unit, Architecture, client suites, OpenAPI/naming contracts, `git diff --check`, `git status --short`, and the affected slice. Do not run or update full discovery while another window is adding tests. Once every writer freezes, update `eng/testing/test-matrix.json` from fresh Release discovery only.

## Self-Review

- Spec coverage: the plan covers immutable persistence, tracked preview orchestration, independent authorization, dual-provider API/migration recovery, strict shared contracts, Vue/Layui parity, E2E, and truthful roadmap closure.
- Scope control: compatibility preview remains unchanged; Apply, rollback, Worker, source persistence, caching, Outbox, and remote repository access remain outside this slice.
- Type consistency: `CodeGenerationRunPreviewRequest`, `CodeGenerationRunResponse`, `CodeGenerationRunPreviewResponse`, operation/status codes, permissions, endpoints, and client unions use the same names across all tasks.
- Placeholder scan: no TBD/TODO or unspecified error handling remains; every failure class has explicit storage and response behavior.

## Completion Evidence

- 2026-07-31：RunService 对残缺模板引用完成真实 RED（2/2 失败）→ GREEN，模板来源聚焦 **9/9**；完整 CodeGeneration Unit 聚焦 **230/230**。
- 045 双库恢复 **2/2**，每个 Provider 在同一测试内覆盖缺失索引与错误列序索引的无损修复；Run API 双 Provider **2/2**，验证 read/execute 权限分离、可信 `sub` actor、成功/失败摘要、单往返分页与历史无源码。
- 共享客户端契约 **3/3**，Vue 聚焦 **7/7**，Layui 聚焦 **4/4**；三个生产构建均通过。
- 复用既有代码生成真实栈文件扩展运行历史验证，避免再创建一套慢 E2E；Vue/Layui 合计 **4/4**，覆盖受跟踪预览、刷新回读、历史无源码与受限账号拒绝。
- affected `inner` 与 `slice` 均为工具链 **39/39**、治理 **16/16**、Release Integration build **0 warning / 0 error**、CodeGeneration + migration-045 + Realtime 组合 **35/35**。
- 新鲜 discovery：Unit **840**、Architecture **50**、Integration full **247**（API SQL Server **43**、API MySQL **43**、migrations **78**、infrastructure **83**）；矩阵契约 **4/4**、分片无遗漏或重复。
- Release solution build **0 warning / 0 error**；Architecture **50/50**；命名门禁 **24/24**。
- 测试反馈：仓库文档示例中的 `pnpm ... -- --snapshot ...` 会把分隔符作为业务参数并报“未知参数”；本切片使用 `pnpm ... --snapshot ...` 完成验证。该问题命中规则/工具一致性候选，但为避免在功能切片末尾扩大治理范围，留待独立测试反馈切片修正，未新增或膨胀 Skill。
