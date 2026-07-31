# CodeGeneration Host CRUD 预览管理页实施计划

> **For agentic workers:** 当前主任务在共享脏工作区内按 TDD 逐项执行；
> 不创建 worktree、不委派子任务。任务快照为
> `codegeneration-client-artifact-apply-20260730`。

**Goal:** 增加 Host 管理员可用的 CRUD Schema 只读预览 API，并在 Vue 与
Layui 双管理端提供同流程页面，展示生成器真实产物而不修改仓库或数据库。

**Architecture:** 新建规格已批准的单一
`Full.NET.Modules.CodeGeneration` 主项目，依赖无 Web 的
`Full.NET.Data.CodeGeneration` 引擎。模块把 HTTP 请求显式映射成
`FullNetCrudSchema`，返回按稳定路径排序的内存产物；客户端只发送受控 JSON，
服务端授权仍是唯一安全边界。

**Tech Stack:** .NET 10、Minimal API、System.Text.Json 源生成、MSTest、
Vue 3 + TypeScript + Element Plus、Layui ESM、Playwright 契约基线。

## Global Constraints

- 权限码固定为 `codegen.previews.read`，作用域为 Host。
- API 固定为 `POST /api/v1/code-generation/previews`；POST 表示创建临时预览，
  不授予写盘、迁移或数据库访问能力。
- 请求最多 128 个字段；数据作用域和标量类型只接受显式稳定机器码。
- 响应只包含规范表名、权限码和生成器当前产物；不返回异常、机器路径或配置。
- Vue 与 Layui 使用同一共享 TypeScript 契约、同一动态导航 component key
  `code-generation-previews` 和同一路径 `/code-generation/previews`。
- 本切片不新增数据库表、迁移、模板持久化、生成任务记录或 Apply 权限。

---

### Task 1: 模块预览契约与纯服务

**Files:**

- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Full.NET.Modules.CodeGeneration.csproj`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Contracts/CodeGenerationPreviewContracts.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Features/PreviewCrudGeneration/CodeGenerationPreviewService.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Properties/AssemblyInfo.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationPreviewServiceTests.cs`

**Interfaces:**

- Produces: `CodeGenerationPreviewService.Preview(...)`
- Produces: `CodeGenerationPreviewRequest` and `CodeGenerationPreviewResponse`
- Consumes: `FullNetCrudSchema.CreateProject(...)` and
  `CrudArtifactGenerator.Generate(...)`

- [x] 先增加成功预览、非法机器码、超过 128 字段、取消和无写盘副作用测试。
- [x] 运行聚焦 Unit，确认因模块和服务缺失而 RED。
- [x] 实现请求到领域 Schema 的封闭映射和稳定 artifact kind 映射。
- [x] 复跑服务测试并确认 GREEN。

### Task 2: Host 权限、Endpoint 与运行时装配

**Files:**

- Create: `src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationModule.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/CodeGenerationAuthorizationContributor.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Features/PreviewCrudGeneration/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.CodeGeneration/Serialization/CodeGenerationJsonSerializerContext.cs`
- Modify: `src/Composition/Full.NET.Composition/Full.NET.Composition.csproj`
- Modify: `src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs`
- Modify: `Full.NET.slnx`
- Modify: `tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationModuleRegistrationTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/CodeGenerationApiSqlServerTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/CodeGenerationApiMySqlTests.cs`

**Interfaces:**

- Produces: permission `codegen.previews.read`
- Produces: `POST /api/v1/code-generation/previews`
- Produces: navigation `code-generation-previews`

- [x] 先增加模块注册、显式授权、匿名拒绝和双 Provider 成功响应测试并观察 RED。
- [x] 实现模块 DI、授权目录、导航和源生成 JSON 注册。
- [x] 接入 Composition API Profile；Migrator/Worker 不注册运行服务。
- [x] 复跑模块 Unit 与 SQL Server/MySQL 聚焦 API 测试并确认 GREEN。

### Task 3: OpenAPI 与共享客户端契约

**Files:**

- Create: `contracts/openapi/codegeneration-preview-v1.json`
- Create: `tests/openapi/codegeneration-preview-contract.test.mjs`
- Create: `tests/Full.NET.IntegrationTests/Api/OpenApiCodeGenerationPreviewContractAssertions.cs`
- Create: `packages/client-contracts/src/code-generation.ts`
- Create: `packages/client-contracts/tests/code-generation.test.ts`
- Modify: `packages/client-contracts/src/index.ts`

**Interfaces:**

- Produces: runtime guards `isCodeGenerationPreviewRequest(...)` and
  `isCodeGenerationPreviewResponse(...)`
- Consumes:公开 HTTP camelCase JSON

- [x] 先增加响应 guard、请求 guard 和 OpenAPI 精确契约测试并观察 RED。
- [x] 实现共享类型、128 字段边界与响应结构 guard。
- [x] 锁定请求/响应 Schema、权限和标准 ProblemDetails。
- [x] 复跑 client-contracts 与 OpenAPI 契约测试并确认 GREEN。

### Task 4: Vue 与 Layui 双管理端

**Files:**

- Create: `ui/admin/src/api/code-generation.ts`
- Create: `ui/admin/src/api/code-generation.test.ts`
- Create: `ui/admin/src/views/CodeGenerationPreviewView.vue`
- Create: `ui/admin/src/views/CodeGenerationPreviewView.test.ts`
- Modify: `ui/admin/src/router/index.ts`
- Modify: `ui/admin/src/navigation/catalog.ts`
- Create: `ui/admin-layui/js/core/code-generation-previews.js`
- Create: `ui/admin-layui/tests/code-generation-previews.test.js`
- Modify: `ui/admin-layui/js/core/route-controllers.js`
- Modify: `ui/admin-layui/js/core/navigation.js`
- Modify: `ui/admin-layui/index.html`
- Modify: `packages/client-contracts/src/navigation-catalog.ts`
- Modify: `packages/client-contracts/tests/navigation-catalog.test.ts`
- Modify: `packages/admin-i18n/src/messages.ts`

**Interfaces:**

- Produces: Vue route name `code-generation-previews`
- Produces: Layui controller `createCodeGenerationPreviewsController`
- Consumes: `POST /api/v1/code-generation/previews`

- [x] 先增加双端 API、导航白名单、JSON 解析失败、预览成功和 ProblemDetails 测试。
- [x] 运行 Vue/Layui/client-contracts 聚焦测试并观察 RED。
- [x] 实现同一示例 Schema、预览提交、artifact 选择与安全纯文本代码展示。
- [x] 复跑双端测试、Vue typecheck 与 Layui production build。

### Task 5: 切片收口

**Files:**

- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify only after fresh discovery: `eng/testing/test-matrix.json`

- [x] 运行任务快照 affected `slice`，只执行选择器命中的聚焦集合。
- [x] 运行 Release solution build、全量 Unit、Architecture、客户端测试和 OpenAPI。
- [x] 用新鲜 discovery 更新唯一测试矩阵，并执行 partitions/governance 契约。
- [x] 检查 `git diff --check`、Docker、分支和共享工作区状态。
- [x] 路线图只记录“只读预览已实现”，模板管理、任务记录、写盘与 E2E 仍保持开放。
- [x] 检查规则/Skill 演进触发条件；未命中时不修改治理文件。
