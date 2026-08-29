# Workflow First Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `fullnet-module-delivery` and `superpowers:test-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付定义与表单不可变版本、启动实例、单人待办同意/拒绝、Vue 安全表单运行时以及双库/AOT 闭环；本计划不交付可视化设计器。

**Architecture:** 创建单主项目 `Full.NET.Modules.Workflow`，用 Dapper 与显式 SQL 拥有 `fn_workflow_*` 数据。定义 Draft 由服务端编译为规范 Workflow IR，表单 Draft 编译为 `WorkflowFormSchema`；实例固定绑定两个发布版本，Todo 动作与表单 Patch 在同一模块事务推进。没有真实跨模块消费者前不拆 Contracts 项目、不创建占位事件。

**Tech Stack:** .NET 10、Dapper、DbUp、System.Text.Json Source Generation、MemoryPack（仅真实 Integration Event）、Vue 3/TypeScript/Element Plus、SQL Server、MySQL、Native AOT。

## Global Constraints

- 批准依据：[`2026-08-20-workflow-module-design.md`](../specs/2026-08-20-workflow-module-design.md)，状态必须保持 Approved。
- 只创建 `Full.NET.Modules.Workflow` 一个模块项目；不存在真实编译期消费者时不创建 `*.Contracts`、`.Http` 或 `.Worker` 项目。
- 新表逻辑主键为应用端 UUID v7；SQL Server 使用 `uniqueidentifier`，MySQL 使用 `BINARY(16)`；两库迁移成对交付。
- API 使用标准状态码、ProblemDetails、稳定 operationId 和 System.Text.Json 源生成；不得使用 Admin.NET 统一包络。
- Host/Tenant 作用域来自受信上下文；Todo 办理同时校验精确权限与资源归属。
- 首版拒绝为终态；首批节点仅 `start`、`human.approval`、`notify.cc`、`gateway.exclusive`、`end`。
- 表单只允许批准的基础字段；客户端不得提交 FormJson、NodeType 能力或字段权限。
- 只修改 `ui/admin`；`ui/admin-layui` 保持零 diff。
- 开工第一步运行 `pnpm test:task:start -- workflow-first-vertical-slice-20260830`；开工检查确认 101 已由 Identity 占用，Workflow 使用两库共同空闲的 `102_WorkflowFirstVerticalSlice.sql`。

---

## File Map

### 新建模块

- `src/Modules/Full.NET.Modules.Workflow/Full.NET.Modules.Workflow.csproj`
- `src/Modules/Full.NET.Modules.Workflow/WorkflowModule.cs`
- `src/Modules/Full.NET.Modules.Workflow/WorkflowAuthorizationContributor.cs`
- `src/Modules/Full.NET.Modules.Workflow/Contracts/WorkflowContracts.cs`
- `src/Modules/Full.NET.Modules.Workflow/Contracts/WorkflowPermissions.cs`
- `src/Modules/Full.NET.Modules.Workflow/Contracts/WorkflowErrorCodes.cs`
- `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowDefinitionCompiler.cs`
- `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowFormCompiler.cs`
- `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowStateMachine.cs`
- `src/Modules/Full.NET.Modules.Workflow/Auditing/WorkflowDomainAuditWrite.cs`
- `src/Modules/Full.NET.Modules.Workflow/Auditing/WorkflowDomainAuditWriter.cs`
- `src/Modules/Full.NET.Modules.Workflow/Auditing/WorkflowDomainAuditActionKeys.cs`
- `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowRecords.cs`
- `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowSql.cs`
- `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowSqlParameters.cs`
- `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowDapperAotMaterializerContributor.cs`
- `src/Modules/Full.NET.Modules.Workflow/Serialization/WorkflowJsonSerializerContext.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/Endpoint.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/WorkflowDefinitionService.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageForms/Endpoint.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageForms/WorkflowFormService.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageInstances/Endpoint.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageInstances/WorkflowInstanceService.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyTodos/Endpoint.cs`
- `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyTodos/WorkflowTodoService.cs`
- `src/Modules/Full.NET.Modules.Workflow/Resources/WorkflowErrors.resx`
- `src/Modules/Full.NET.Modules.Workflow/Resources/WorkflowErrors.en-US.resx`

### 迁移、注册、测试与客户端

- `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/102_WorkflowFirstVerticalSlice.sql`
- `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/102_WorkflowFirstVerticalSlice.sql`
- `Full.NET.slnx`
- `src/Composition/Full.NET.Composition/Full.NET.Composition.csproj`
- `src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs`
- `src/Composition/Full.NET.Composition/FullNetModuleSelection.cs`
- `tests/Full.NET.UnitTests/Workflow/WorkflowDefinitionCompilerTests.cs`
- `tests/Full.NET.UnitTests/Workflow/WorkflowFormCompilerTests.cs`
- `tests/Full.NET.UnitTests/Workflow/WorkflowStateMachineTests.cs`
- `tests/Full.NET.UnitTests/Workflow/WorkflowAuthorizationContributorTests.cs`
- `tests/Full.NET.ArchitectureTests/WorkflowBoundaryTests.cs`
- `tests/Full.NET.IntegrationTests/Workflow/WorkflowFirstSliceAssertions.cs`
- `tests/Full.NET.IntegrationTests/Workflow/WorkflowFirstSliceSqlServerTests.cs`
- `tests/Full.NET.IntegrationTests/Workflow/WorkflowFirstSliceMySqlTests.cs`
- `tests/Full.NET.IntegrationTests/Migrations/WorkflowMigrationRecoveryAssertions.cs`
- `ui/admin/src/api/workflow.ts`
- `ui/admin/src/api/workflow.test.ts`
- `ui/admin/src/views/WorkflowDefinitionsView.vue`
- `ui/admin/src/views/WorkflowDefinitionsView.test.ts`
- `ui/admin/src/views/WorkflowInstancesView.vue`
- `ui/admin/src/views/WorkflowInstancesView.test.ts`
- `ui/admin/src/views/WorkflowTodosView.vue`
- `ui/admin/src/views/WorkflowTodosView.test.ts`
- `ui/admin/src/features/workflow/forms/WorkflowFormRenderer.vue`
- `ui/admin/src/features/workflow/forms/WorkflowFormRenderer.test.ts`
- `tests/e2e/admin-real-stack/tests/workflow.spec.mjs`
- `tests/Full.NET.IntegrationTests/NativeAot/NativeApiWorkflowE2EAssertions.cs`
- `tests/Full.NET.IntegrationTests/NativeAot/NativeApiWorkflowSqlServerE2ETests.cs`
- `tests/Full.NET.IntegrationTests/NativeAot/NativeApiWorkflowMySqlE2ETests.cs`

## Stable Interfaces

```csharp
internal sealed record WorkflowDefinitionDraft(
    int SchemaVersion,
    IReadOnlyList<WorkflowNodeDraft> Nodes);

internal sealed record WorkflowNodeDraft(
    string NodeKey,
    string NodeTypeKey,
    int NodeSchemaVersion,
    JsonElement Config);

internal sealed record WorkflowFormSchema(
    int SchemaVersion,
    int AdapterVersion,
    IReadOnlyList<WorkflowFormSection> Sections);

internal sealed record WorkflowFormSection(
    string SectionKey,
    IReadOnlyList<WorkflowFormField> Fields);

internal sealed record WorkflowFormField(
    string FieldKey,
    string FieldTypeKey,
    bool Required,
    IReadOnlyDictionary<string, JsonElement> Constraints);

internal sealed record StartWorkflowCommand(
    Guid DefinitionVersionId,
    string BusinessType,
    string BusinessId,
    IReadOnlyDictionary<string, JsonElement> InitialValues,
    string IdempotencyKey);

internal sealed record ActOnWorkflowTodoCommand(
    Guid TodoId,
    long ExpectedRevision,
    IReadOnlyDictionary<string, JsonElement> FieldPatch,
    string? Comment,
    string IdempotencyKey);
```

HTTP Request、内部 Command 和持久化 Record 必须是不同类型。上述类型是计划要求的稳定语义；实现时可按现有模块可见性拆文件，但不得改变 FieldKey Patch、ExpectedRevision、IdempotencyKey 和服务端重新校验边界。

---

### Task 1: 建立编译器和状态机 RED 合同

**Files:** Unit 测试四个文件；创建 Domain 编译器与状态机的最小类型文件。

**Produces:** `WorkflowDefinitionCompiler.Compile`、`WorkflowFormCompiler.Compile`、`WorkflowStateMachine.Start/Approve/Reject` 的闭合输入输出。

- [x] 编写失败测试：同一 Draft 重排无语义对象键后得到相同规范 JSON 与 SHA-256 Hash。
- [x] 编写失败测试：未知 NodeType、重复 NodeKey、悬空引用、不可达节点、无终点和非法回边均返回稳定错误码。
- [x] 编写失败测试：表单未知组件、重复 FieldKey、脚本/CSS/HTML/远程数据源、非法金额 Scale 和危险 VForm 扩展均发布失败。
- [x] 编写失败测试：Approve 只能处理本人 Active Todo；Reject 进入终态；重复 IdempotencyKey 返回同一结果；旧 Revision 冲突。
- [x] 运行 `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Workflow"`，确认因实现缺失失败且测试被发现。
- [x] 实现最小纯函数编译器与状态机，不访问数据库、HTTP、当前时间或随机数。
- [x] 重跑同一命令并确认全部 Workflow Unit 通过。

### Task 2: 创建模块骨架、权限与静态闭包

**Files:** 模块 csproj、Module、Authorization、Contracts、Resources、Serialization、Composition、Solution 和 Architecture 测试。

**Produces:** `WorkflowModule`、稳定权限/错误码目录、源生成 JSON Context 和主机注册。

- [x] 先写 Architecture/Unit RED：Workflow 只能引用批准的 BuildingBlocks；不得引用其他业务模块、具体 SQL Driver、反射多态或动态代码；权限目录必须包含 Spec §10 的每个页面/操作码。
- [x] 创建单主项目并加入 `Full.NET.slnx` 与 Composition；Api/Migrator 使用完整模块入口，Worker 仅在后续有真实恢复消费者时增加最小入口。
- [x] 创建中文/英文错误资源；错误码覆盖 schema invalid、version conflict、not published、active instance exists、todo forbidden、revision conflict 和 invalid transition。
- [x] 将当前切片的所有 JSON 闭合类型登记到 `WorkflowJsonSerializerContext`；HTTP DTO 与 ProblemDetails 扩展随首次 Endpoint 在对应任务登记，AOT 编译不得回退反射。
- [x] 运行 Workflow Architecture、Authorization Unit 与 `dotnet build ... -p:FullNetAotAnalysis=true`，确认零新增 AOT/裁剪警告。

### Task 3: 成对迁移与 Dapper 持久化

**Files:** 两个 `102_WorkflowFirstVerticalSlice.sql`、Persistence 文件和 Migration/Integration 测试。

**Produces:** Spec §7 的十三张表、双库等价约束、稳定 StatementName 和 AOT 物化器。

- [ ] 开工时运行迁移号检查；若 101 已占用，停止并先把本计划与两库文件名改为同一个空闲号。
- [ ] 先写双库 RED：全新迁移、DbUp 未记账的部分 DDL、二次执行、数据保留、发布版本不可更新路径、一个 Active 业务键和 Todo 并发修订。
- [ ] 创建十三张 `fn_workflow_*` 表（含独立 Definition Draft、追加式 ActionRecord 和模块自有 DomainAudit）；高写入表使用符合 Naming Profile 的 UUID v7 索引布局，所有唯一/索引/约束在两库等价且通过名称长度门禁。
- [ ] `WorkflowSql` 只含参数化显式 SQL；`WorkflowSqlParameters` 返回静态字典/闭合参数，禁止匿名 SQL 参数和动态物化。
- [ ] 注册所有查询 Record 的 `WorkflowDapperAotMaterializerContributor`。
- [ ] 运行聚焦迁移恢复、SQL Server/MySQL Integration、`pnpm test:naming` 和 `pnpm test:sql-safety`。

### Task 4: 定义与表单 Draft/Publish API

**Files:** ManageDefinitions、ManageForms、Contracts、Serialization 与双库 Integration。

**Consumes:** Task 1 编译器，Task 3 持久化。

**Produces:** 定义/表单 Draft CRUD、不可变 Publish、版本读取和稳定 Hash。

- [ ] 先写双库 RED：Host/Tenant 隔离、跨作用域引用失败、ExpectedRevision、单调 VersionNumber、不可变版本、相同输入 Hash、危险 Schema 失败和直接 API 403。
- [ ] Endpoint 只做授权/传输映射；Service 从受信上下文解析 Scope，在事务内校验 Draft 修订并插入新版本。
- [ ] Definition Publish 必须绑定同作用域已发布 FormVersionId；不存在、未发布或跨租户引用失败关闭。
- [ ] API 不接收 Published 标记、Hash、能力目录、字段权限或 TenantId 作为权威输入。
- [ ] 运行双库聚焦 Integration、OpenAPI 快照/operationId 和 JSON 源生成测试。

### Task 5: Start、Todo 和表单 Patch 原子闭环

**Files:** ManageInstances、ManageMyTodos、Domain、Persistence 与 Integration。

**Produces:** Start、mine/detail、approve/reject、execution log 和 form submission。

- [ ] 先写双库 RED：启动固定版本、同业务键最多一个 Active、历史终态可重开、本人 Todo、越权 403、隐藏/只读/未知 Patch 失败、必填/类型失败、旧 Revision、重复/并发同意拒绝确定收敛。
- [ ] Start 在一个本地事务创建 Instance、首 Step、Todo、Submission、ExecutionLog 和 `fn_workflow_domain_audit` B0 记录；没有真实外部消费者时不写占位 Outbox。
- [ ] Approve/Reject 在一个本地事务校验资源授权、Revision、Idempotency、字段策略和 Schema，更新 Submission/Todo/Step/Instance，并追加 ActionRecord、ExecutionLog 与 DomainAudit；审批意见不能只保存在 Todo 或日志字符串中。
- [ ] 事务中禁止调用 Identity、Organization、Notifications、Files 或外部服务；需要的主体/文件验证在事务前完成并在提交时重验本模块不变量。
- [ ] 运行双库 Integration，确认 SQL Server/MySQL 并发结果与 ProblemDetails 一致。

### Task 6: Vue 只读定义/实例、启动与待办运行时

**Files:** `ui/admin/src/api/workflow*`、三个 View、`WorkflowFormRenderer*`、路由/导航目录及生成客户端清单。

**Produces:** 无可视化设计器的首切片管理/办理体验；Schema 只能来自服务端。

- [ ] 先写 Vue RED：每个页面权限、approve/reject 独立权限、无权按钮不进入 DOM、403/409/422 ProblemDetails、重复提交保护、隐藏/只读/必填字段和服务端错误恢复。
- [ ] API Adapter 只调用 OpenAPI 生成 Operation 并对 `unknown` 响应执行运行时守卫；不手写第二套路径或 DTO。
- [ ] `WorkflowFormRenderer` 使用本地静态基础组件目录解释 `WorkflowFormSchema`；不得使用 `v-html`、动态代码或允许页面替换 FormJson。
- [ ] Definition 页面首切片只读展示定义/版本与发布状态；Draft 的可视化编辑由设计器计划交付，禁止增加临时 JSON textarea 作为产品能力。
- [ ] 运行 `pnpm --filter @fullnet/admin test`、typecheck、生产构建、`pnpm test:bundle-budgets`、客户端审计和许可证检查。

### Task 7: 真实栈、Native AOT 与切片关闭

**Files:** admin-real-stack Workflow 场景、Native AOT 外部进程 Workflow 场景、Verification 与状态文档。

**Produces:** 双库发布→启动→办理闭环的可重复证据。

- [ ] E2E 测试通过 API 建立批准的 Definition/Form 版本，再在 Vue 完成启动与审批；不依赖尚未实现的可视化设计器。
- [ ] SQL Server/MySQL 各覆盖 Host 和 Tenant：approve、reject、无权限按钮缺失、直接 API 403、旧 Revision 409、危险 Patch 422 和刷新后权威状态。
- [ ] Linux 原生 Host.Api 外部进程覆盖 Workflow HTTP/JSON/Dapper 双库路径；Worker 恢复未实现时明确保持未验证，不借 API AOT 证据升格。
- [ ] 使用任务快照先运行 `pnpm test:integration:affected:plan -- --snapshot workflow-first-vertical-slice-20260830 --phase slice`，审查后运行 `pnpm test:slice -- --snapshot workflow-first-vertical-slice-20260830`；禁止本地全量替代受影响集。
- [ ] 新建 dated Verification，记录基线、环境、命令、非零发现数、原始结果、容量未验证项和许可证状态。
- [ ] 只有新鲜证据满足 Spec 后，才把 Workflow 从 Planned/Designing 更新到最高真实状态；不得因本计划批准标记为 Implemented。

---

## Stop Conditions

- Spec 状态不再是 Approved、迁移号冲突、需要跨模块本地事务、需要任意脚本/远程 URL、需要客户端决定权限或需要未批准第三方依赖时立即停止。
- VForm3/Workflow-Vue3 不属于本计划；任何为了演示而提前迁入设计器源码的 diff 都必须移出本切片。
- SQL Server/MySQL 任一环境无法验证数据库行为时，不关闭切片、不声称双库通过。
- Native AOT 需要反射 fallback、运行时程序集扫描或未闭合 native binding 时停止并修订设计。

## Completion Evidence

- Unit、Architecture、双库 Migration/Integration、OpenAPI/JSON、Vue、bundle、许可证和受影响 slice 均有新鲜非零输出。
- `git diff --check` 与任务快照影响集干净；无 `ui/admin-layui/**` 新功能差异。
- Verification 明确区分 Build-verified、Aot-published、Verified 和 `Capacity-not-verified`。
