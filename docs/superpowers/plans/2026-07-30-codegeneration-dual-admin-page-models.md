# CodeGeneration Dual Admin Page Models Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为代码生成器补齐 Vue 与 Layui 双管理端 CRUD 页面模型，使宿主页面可以直接复用同一套分页、权限、写入、乐观并发和错误处理契约。

**Architecture:** 生成器继续保留现有 API 客户端作为唯一 HTTP 边界，并新增两个使用已登记后缀的页面模型产物：Vue Composition API 模型和无框架依赖的 Layui 控制器模型。页面模型只管理状态与动作，不生成视觉组件，也不修改路由、菜单、国际化资源或宿主工程，从而避免把生成器扩张为第二套 UI 框架。

**Tech Stack:** .NET 10、C#、MSTest、Vue 3 Composition API、TypeScript、ES Modules、Layui 宿主适配。

## Global Constraints

- 生成文件只使用现有 `.generated.ts`、`.generated.js` 和 `.g.cs` 后缀。
- Vue 与 Layui 必须复用各自已生成的 API 客户端、相同权限码和相同稳定错误码。
- 页面模型不得自动写入菜单、路由、翻译文本、宿主依赖注入或手写页面。
- 写动作必须在页面模型内部再次检查写权限，并防止同一模型的重复并发提交。
- 有 `Version` 的 Schema 必须由页面模型从当前行回填版本，调用方不得手写乐观并发版本。
- 不新增规则、Skill、测试门槛或能力状态文档；本切片只扩展现有生成器能力。

---

### Task 1: Specify dual page-model artifacts

**Files:**
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`
- Modify: `docs/superpowers/plans/2026-07-30-codegeneration-dual-admin-page-models.md`

**Interfaces:**
- Consumes: `CrudArtifactGenerator.Generate(FullNetCrudSchema schema)`.
- Produces: `clients/vue/{resource}-page.generated.ts` and `clients/layui/{resource}-page.generated.js`.

- [ ] **Step 1: Write failing artifact and contract tests**

Add assertions that both page-model paths exist and verify:

```csharp
StringAssert.Contains(vuePage, "export function useProductPage");
StringAssert.Contains(vuePage, "Omit<UpdateProductRequest, 'version'>");
StringAssert.Contains(vuePage, "productPermissions.write");
StringAssert.Contains(layuiPage, "export function createProductPageModel");
StringAssert.Contains(layuiPage, "productPermissions.write");
StringAssert.Contains(layuiPage, "version: item.version");
```

Also assert stable load/operation failure codes, no route/menu mutation API, and valid kebab-case resource imports.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CrudArtifactGeneratorTests"
```

Expected: FAIL because the two page-model artifacts do not exist.

### Task 2: Generate Vue and Layui page models

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudClientPageModelGenerator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudArtifactGenerator.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`

**Interfaces:**
- Consumes: `FullNetCrudSchema`, the existing generated API module beside each page model, and injected permission/error/state callbacks.
- Produces: `CrudClientPageModelGenerator.GenerateVue(FullNetCrudSchema)` and `GenerateLayui(FullNetCrudSchema)`.

- [ ] **Step 1: Add the two generated artifacts**

`CrudArtifactGenerator.Generate` adds:

```csharp
new GeneratedArtifact(
    $"clients/layui/{schema.ApiResourceName}-page.generated.js",
    GeneratedArtifactKind.LayuiClient,
    CrudClientPageModelGenerator.GenerateLayui(schema)),
new GeneratedArtifact(
    $"clients/vue/{schema.ApiResourceName}-page.generated.ts",
    GeneratedArtifactKind.VueClient,
    CrudClientPageModelGenerator.GenerateVue(schema)),
```

- [ ] **Step 2: Implement the Vue model**

Generate a Composition API model with readonly state, computed read/write permission, guarded `load/create/update/disable` actions, current-row version forwarding, and injected `onProblem` callback. `update` accepts `Omit<Update{Entity}Request, 'version'>` when the Schema has `Version`.

- [ ] **Step 3: Implement the Layui model**

Generate an ES module exposing immutable snapshots and guarded `load/create/update/disable` actions. State changes are published through `onStateChange`; errors are published through `onProblem`; DOM rendering remains the host controller's responsibility.

- [ ] **Step 4: Verify GREEN**

Run the Task 1 command and require all `CrudArtifactGeneratorTests` to pass except the fixture drift test, which remains red until Task 3.

### Task 3: Freeze representative generated fixtures

**Files:**
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/clients/vue/products-page.generated.ts`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/clients/layui/products-page.generated.js`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`

**Interfaces:**
- Consumes: deterministic output from `CrudClientPageModelGenerator`.
- Produces: byte-for-byte Catalog/Product fixtures covering tenant-scoped and versioned CRUD.

- [ ] **Step 1: Add exact generated fixtures**

Persist both generated outputs with LF endings and no BOM. Do not hand-edit the generated contract after it matches the generator output.

- [ ] **Step 2: Run focused generator tests**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CrudArtifactGeneratorTests"
```

Expected: all focused tests PASS.

### Task 4: Verify the affected slice

**Files:**
- Inspect: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/**`
- Inspect: `tests/Full.NET.UnitTests/CodeGeneration/**`

**Interfaces:**
- Consumes: task snapshot `codegeneration-dual-admin-pages-20260730`.
- Produces: fresh focused, affected-slice, formatting and build evidence.

- [ ] **Step 1: Run all CodeGeneration unit tests**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGeneration"
```

- [ ] **Step 2: Run affected selector and slice**

```powershell
pnpm test:integration:affected:plan -- --snapshot codegeneration-dual-admin-pages-20260730 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-dual-admin-pages-20260730 --phase slice
```

- [ ] **Step 3: Run static delivery checks**

```powershell
dotnet build src/BuildingBlocks/Full.NET.Data.CodeGeneration/Full.NET.Data.CodeGeneration.csproj -c Release --no-restore
git diff --check
git status --short
```

Expected: build and selected tests pass; `git diff --check` reports no whitespace errors introduced by this task. Report unrelated pre-existing worktree changes separately.
