# CodeGeneration Host Apply Implementation Plan

> **For Codex:** Execute this plan task by task with RED → GREEN evidence. Do not run shared .NET or Docker while another window owns them.

**Goal:** 在 Host 代码生成工作台中，把一次已审查且来源于持久化模板的受跟踪预览安全应用到服务器配置的本地生成工作区，并记录不可变、无源码的 Apply 摘要。

**Architecture:** Apply 只接受 `previewRunId`，不接受客户端路径、内联 Schema 或目标仓库 URL。服务端从成功的 preview 运行记录恢复模板 ID/版本，重新读取同版本模板并确定性生成，再复核 Schema/Manifest 摘要与预览记录完全一致；只有配置显式启用且本地工作区通过启动期校验时，才调用现有 `CrudGenerationWorkspace` 的清单所有权、并发锁、冲突检测和原子提交能力。该切片只写服务器运维配置的本地生成工作区，不执行模块入口、Composition、客户端路由接线，不启动 Worker，不 clone/pull/push 远程仓库。

**Tech Stack:** .NET 10、Minimal API、System.Text.Json 源生成、Dapper、`Full.NET.Data.CodeGeneration` 安全工作区、Vue 3、Layui、TypeScript、Vitest、Playwright。

## Scope and invariants

- 新增独立权限 `codegen.runs.apply`；`codegen.runs.execute` 仍只允许受跟踪预览，read 权限不能写盘。
- Apply 必须绑定成功的 `preview` 运行，且该运行必须引用完整的 `TemplateId + TemplateVersion`；内联 Schema 预览不可 Apply。
- 模板被修改、删除，或重新生成后的 `SchemaSha256` / `ManifestSha256` 与预览不一致时，零写入并返回稳定冲突码。
- 工作区根目录只来自 `CodeGeneration:Apply:WorkspaceRoot`。请求、模板和运行记录都不能覆盖该路径。
- `Enabled=false` 为默认安全状态；启用时启动期要求绝对、本地、已存在目录。API 不创建或猜测仓库根目录。
- `CrudGenerationWorkspace.ApplyAsync` 返回冲突计划时零写入；成功时复用现有 Manifest 最后提交、锁和恢复语义。
- Apply 运行继续写入 `fn_codegen_run`；成对 `046_CodeGenerationApply` 无损扩展 045，先插入 `operationKind=apply/status=running` 审计意图，再通过受控单行 SQL 收敛为 `succeeded/failed`。046 不修改 040–045 所有权。
- 运行记录、响应、日志和管理端历史不得包含 Schema、源码、工作区绝对路径、异常正文或恢复文件内容。
- 首个切片同步执行并设置单进程互斥门禁；Worker/队列化执行、跨实例调度、远程 Git 和模块/Composition/路由接线继续开放。

## File map

- Create `src/Modules/Full.NET.Modules.CodeGeneration/Configuration/CodeGenerationApplyOptions.cs`: 默认禁用的本地工作区配置。
- Create `src/Modules/Full.NET.Modules.CodeGeneration/Configuration/CodeGenerationApplyOptionsValidator.cs`: 启动期 fail-closed 校验。
- Create `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationApplyService.cs`: 预览绑定、摘要复核、互斥写盘和运行摘要编排。
- Create `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationRunSummary.cs`: 统一 Schema/Manifest 摘要计算，避免 Preview/Apply 漂移。
- Modify `src/Modules/Full.NET.Modules.CodeGeneration/Contracts/CodeGenerationRunContracts.cs`: Apply 权限、操作码、请求/响应与稳定错误码。
- Modify `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationRunService.cs`: 复用摘要计算器，不改变兼容预览语义。
- Modify `src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/Endpoint.cs`: `POST /api/v1/code-generation/runs/apply`。
- Modify `src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationModule.cs`, `CodeGenerationAuthorizationContributor.cs`, `Serialization/CodeGenerationJsonSerializerContext.cs`: 配置、DI、权限和 JSON 接线。
- Create `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationApplyOptionsTests.cs` and `CodeGenerationApplyServiceTests.cs`: 配置与服务 RED/GREEN。
- Create paired `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/{SqlServer,MySql}/046_CodeGenerationApply.sql` and `tests/Full.NET.IntegrationTests/Migrations/Migration046CodeGenerationApplyRecoveryTests.cs`: 双库状态机约束、无损重跑与 guarded completion。
- Modify existing CodeGeneration API assertions/classes and `contracts/openapi/code-generation-runs-v1.json`: 双 Provider API、权限和无源码契约。
- Modify `packages/client-contracts/src/code-generation-runs.ts`, its tests and exports: strict Apply DTO/guards.
- Modify Vue/Layui CodeGeneration run adapters, views/controllers and tests: 仅在模板受跟踪预览成功后展示确认 Apply；显示摘要，不显示服务器路径。
- Modify `tests/e2e/admin-real-stack/scripts/bootstrap-stack.mjs`, `spec-contracts.test.mjs`, and existing `host-code-generation-previews.spec.mjs`: 使用测试专属临时工作区并验证双端真实写盘、刷新回读、权限拒绝和清理。
- Create `docs/verification/codegeneration-host-apply-2026-07-31.md`; update only truthful CodeGeneration roadmap rows after all gates pass.

## Task 1: Freeze configuration and public contracts

1. Add Unit REDs for these cases:
   - disabled with no root is valid;
   - enabled with missing, relative, UNC/non-local, file, or nonexistent root fails validation without echoing the root;
   - enabled with an existing absolute local directory succeeds;
   - Apply request contains only a non-empty `previewRunId`;
   - operation union accepts `preview|apply`, and Apply has an independent permission.
2. Run only `CodeGenerationApplyOptionsTests` and the run contract tests; confirm failure is caused by missing types/codes.
3. Implement options and validator. Bind `CodeGeneration:Apply`, register validation on start, and keep the feature disabled by default.
4. Add request/response:
   - `CodeGenerationRunApplyRequest(Guid PreviewRunId)`;
   - `CodeGenerationRunApplyResponse(Guid RunId, Guid PreviewRunId, int ArtifactCount, int ChangedArtifactCount, string ManifestSha256)`.
5. Add stable failures for disabled, invalid preview, preview source not applicable, stale preview, workspace conflict, busy and unexpected apply failure. Messages remain generic English API text; no path/source details.
6. Re-run the focused tests GREEN.

## Task 2: Bind Apply to a reviewed preview and reuse safe workspace semantics

1. Add `CodeGenerationApplyServiceTests` RED cases:
   - missing/failed/non-preview run rejects before generation/write;
   - successful inline-schema preview rejects because it has no durable template source;
   - template missing/deleted/version changed rejects with zero workspace calls;
   - regenerated Schema or Manifest mismatch rejects with zero writes;
   - disabled configuration rejects before touching disk;
   - a conflict plan records failed Apply with stable error code and exposes no path;
   - success applies once, counts only create/update/delete actions as changed, and persists one successful Apply summary;
   - second concurrent request returns busy before disk access;
   - cancellation before commit propagates; after the workspace enters its non-cancellable commit semantics, existing recovery guarantees remain authoritative;
   - run insert affected-row count other than one fails closed.
2. Extract `CodeGenerationRunSummary` from existing preview service logic. It computes the canonical manifest hash from ordered `(path, kind, sha256)` and is used by both preview and Apply.
3. Implement `CodeGenerationApplyService`:
   - load preview via `CodeGenerationRunQueryService`;
   - require successful `preview`, complete template reference and non-null stored hashes;
   - load exact current template version, normalize, generate with `CrudArtifactGenerator`, and compare both hashes;
   - acquire a process-local non-blocking semaphore;
   - call `CrudGenerationWorkspace.ApplyAsync(options.WorkspaceRoot, schema)`;
   - map `CanApply=false` and known workspace conflicts to stable failures;
   - persist a failed or succeeded `apply` summary, never exception text/path/source.
4. Do not alter `GenerationWorkspaceStore`; its extensive existing atomicity/recovery suite remains the write authority.
5. Run `CodeGenerationApplyServiceTests`, existing `CodeGenerationRunServiceTests`, and workspace focused tests GREEN.

## Task 3: Expose the protected endpoint and dual-provider API contract

1. Extend CodeGeneration API assertions with REDs for:
   - `POST /api/v1/code-generation/runs/apply` requires `codegen.runs.apply`;
   - execute-only and read-only identities receive 403;
   - trusted actor comes only from `sub`;
   - successful template preview → Apply writes one `apply` history row and returns hashes/counts;
   - stale preview and workspace conflict return ProblemDetails and zero source/path leakage;
   - run list/get returns both operation kinds without changing single-round-trip pagination.
2. Map the endpoint and register services/options/serializer DTOs. Use `IApiResultMapper`; do not add an Admin.NET envelope.
3. Update the OpenAPI fixture and runtime assertion.
4. 先运行 046 双库恢复 RED，证明 045 会拒绝 `apply/running`；新增成对迁移后验证既有 preview 数据保留、running 只允许一次 guarded completion，删除 DbUp 记账重跑仍保留终态。
5. For Integration hosts, inject a test-owned temporary local workspace through configuration and clean it in fixture disposal. Never point tests at the checkout root.
6. Run the existing SQL Server and MySQL CodeGeneration API test methods, 046 recovery and OpenAPI contract checks.

## Task 4: Add strict client contracts and both management clients

1. Add client-contract REDs for Apply request/response guards, `apply` operation union, absent/extra/invalid fields and invalid SHA/counts.
2. Add Vue and Layui API adapter REDs for exact request path/body and malformed response rejection.
3. Extend both management views/controllers:
   - retain the last successful tracked template preview `runId`;
   - clear it whenever input/template selection changes or preview fails;
   - show a destructive confirmation containing template/version and manifest short hash;
   - call Apply only after confirmation and permission check;
   - refresh history and show result counts/hash; never show workspace path;
   - read/execute/apply permissions remain independently rendered.
4. Run client-contracts, Vue, and Layui focused suites and production builds.

## Task 5: Prove the real browser flow without writing into the repository

1. Extend bootstrap contract RED to create a unique temporary CodeGeneration Apply workspace, pass `CodeGeneration__Apply__Enabled=true` and `WorkspaceRoot`, persist only its test-state identifier/path for teardown, and recursively remove exactly that verified test-owned directory during teardown.
2. Reuse `host-code-generation-previews.spec.mjs`; do not create another slow E2E file.
3. For Vue and Layui:
   - create/select a template;
   - run tracked preview;
   - confirm Apply;
   - assert success/history after reload;
   - read the test workspace from Node and verify manifest plus representative artifacts match expected hashes;
   - assert API/UI never returns the absolute workspace root or source body in history;
   - assert a restricted account cannot Apply.
4. Run the single CodeGeneration E2E file for both management projects and verify teardown removes the temp workspace and leaves runner/Docker/Ryuk counts zero.

## Task 6: Close the slice and record fresh evidence

1. Run focused CodeGeneration Unit, Architecture, OpenAPI, naming and all three client suites.
2. Run `pnpm test:integration:affected:plan --snapshot codegeneration-host-apply-20260731 --phase inner`, inspect the selector, then affected `inner` and `slice` only after shared resources are released. The repository wrapper currently rejects the documented extra `--` separator; use the script's verified actual syntax and record the discrepancy, without expanding rules in this feature slice.
3. Run `git diff --check`, task-scoped status review, and explicit runner/Docker residual audit.
4. After every writer freezes, update `eng/testing/test-matrix.json` from fresh Release discovery only; never hand-calculate from 840/861 or other stale counts.
5. Mark “管理端 Apply 权限” complete only if backend, independent authorization, safe local workspace, both clients and real browser E2E pass. Keep Worker execution, rollback, module/Composition/route integration, remote repository writes and production rollout open.
6. Create the verification record with actual commands/results. Check rule/skill evolution triggers; ordinary success does not add governance text.

## Acceptance checklist

- [x] Apply input cannot select a path or submit inline source.
- [x] Apply is cryptographically bound to a successful persisted-template preview.
- [x] Disabled/stale/conflict/busy/error paths are zero-write and path/source safe.
- [x] Local workspace write reuses existing manifest ownership, locking and recovery semantics.
- [x] Apply permission is independent from preview execute and history read.
- [x] SQL Server/MySQL API behavior and 046 running→terminal recovery semantics are equivalent.
- [x] Vue and Layui require explicit confirmation and refresh immutable history.
- [x] Real-stack E2E writes only to a disposable test-owned directory and cleans it.
- [x] Worker, rollback, Git remote operations and module integration remain explicitly out of scope.
