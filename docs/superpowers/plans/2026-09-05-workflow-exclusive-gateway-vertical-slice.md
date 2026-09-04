# Workflow 排他网关纵向切片实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不引入脚本、远程调用或通用表达式引擎的前提下，让 `gateway.exclusive` 可发布、可执行、可在 Workflow-Vue3 设计器中配置，并以 SQL Server/MySQL、Vue 与 Native AOT 证据关闭首个排他分支切片。

**Architecture:** 网关配置保留在不可变 Workflow IR 中，使用有界、有序、单谓词分支和唯一默认分支。服务端发布时结合绑定的 `WorkflowFormSchema` 校验字段、操作符和值类型，运行时只读取实例的已验证表单提交，选择首个命中分支并同步记录网关步骤与执行日志。Workflow 仍只写 `fn_workflow_*`，不调用其他模块或外部 Provider。

**Tech Stack:** .NET 10、System.Text.Json 源生成、Dapper 显式 SQL、MSTest、Vue 3、TypeScript、Element Plus、Workflow-Vue3 受控来源、Vitest、Playwright、GitHub Actions。

## Global Constraints

- 任务基线：`65793cc18432dc33609f7b5dd93f44b69dc18375`；任务快照：`workflow-gateway-exclusive-20260905`。
- `gateway.exclusive` 只允许 1–15 个有序条件分支和 1 个默认分支；条件分支按数组顺序首次命中，所有目标节点必须与 `nextNodeKeys` 精确一致。
- 首切片每个条件分支只允许一个声明式谓词：`equals`、`notEquals`、`greaterThan`、`greaterThanOrEqual`、`lessThan`、`lessThanOrEqual`、`isEmpty`、`isNotEmpty`；操作符必须与绑定表单字段类型兼容。
- 禁止 JavaScript、表达式字符串、任意对象遍历、HTTP、SQL、回调、跨模块读取和客户端能力声明；未知配置字段失败关闭。
- 网关判断使用实例绑定的不可变表单版本；审批后的网关必须读取当前动作 Field Patch 合并并通过服务端校验后的提交值。
- 网关、抄送和下一审批/终点属于同一个 Workflow 本地事务；拒绝不执行任何下游网关或抄送。
- 不新增数据库表或迁移；复用 `fn_workflow_step` 与 `fn_workflow_execution_log`，网关步骤状态同步写为 `completed`。
- 后端新增或修改的类型、构造函数、方法、参数和关键业务块必须有中文 XML/行内说明；稳定标识符使用英文。
- Vue 是唯一新增后台交付线；`ui/admin-layui` 保持零修改。
- 完整双库、真实浏览器与 Linux Native AOT 重型验证提交后交给 GitHub Actions；本地只执行受影响内循环。

---

### Task 1: 闭合网关配置与条件求值

**Files:**
- Create: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowExclusiveGatewayConfiguration.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowFormValueValidator.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Contracts/WorkflowErrorCodes.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Resources/WorkflowErrors.resx`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Resources/WorkflowErrors.en-US.resx`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowExclusiveGatewayConfigurationTests.cs`

**Interfaces:**
- Produces: `WorkflowExclusiveGatewayConfiguration.TryRead(JsonElement, WorkflowFormSchema?, out WorkflowExclusiveGatewayDefinition?)`。
- Produces: `WorkflowExclusiveGatewayDefinition.TrySelectBranch(IReadOnlyDictionary<string, JsonElement>, out WorkflowExclusiveGatewaySelection)`。
- Produces: `WorkflowFormValueValidator.IsFieldValueValid(WorkflowFormField, JsonElement)`，供发布期复用现有字段线格式校验。

- [x] **Step 1: 写 RED 配置与求值测试**

  覆盖金额比较、整数比较、字符串/布尔相等、可空字段空值、首个命中、默认分支，以及未知字段、未知操作符、重复分支键、缺少默认分支、目标集合漂移、未知配置字段、脚本/URL 字段失败关闭。

- [x] **Step 2: 运行 RED 测试**

  Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter FullyQualifiedName~WorkflowExclusiveGatewayConfigurationTests`

  Expected: FAIL，因为配置解析器和新错误码尚不存在。

- [x] **Step 3: 实现最小闭合解析与类型化比较**

  配置线格式固定为：

  ```json
  {
    "nodeName": "金额分流",
    "nextNodeKeys": ["finance", "manager"],
    "branches": [{
      "branchKey": "large-amount",
      "nextNodeKey": "finance",
      "condition": {
        "fieldKey": "amount",
        "operator": "greaterThanOrEqual",
        "value": "1000.00"
      }
    }],
    "defaultNextNodeKey": "manager"
  }
  ```

  所有数组和字符串长度有界；金额/小数、日期时间和选择项复用表单协议的规范线格式，不做文化相关转换。

- [x] **Step 4: 运行 GREEN 测试与 Workflow 错误资源测试**

  Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~WorkflowExclusiveGatewayConfigurationTests|FullyQualifiedName~WorkflowLocalization"`

  Expected: PASS。

### Task 2: 编译器与运行计划支持排他分支

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowNodeTypeCatalog.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowDefinitionCompiler.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowRuntimePlan.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowNodeTypeCatalogTests.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowDefinitionCompilerTests.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowRuntimePlanTests.cs`

**Interfaces:**
- Consumes: Task 1 的闭合网关定义与选择结果。
- Produces: `WorkflowRuntimePlan.TryCreate(WorkflowDefinitionDraft, WorkflowFormSchema?, out WorkflowRuntimePlan?)`。
- Produces: 带 `AutomaticNodes` 的 `WorkflowApprovalTransition`，自动节点按真实路径顺序携带 `notify.cc` 或 `gateway.exclusive` 的必要数据。

- [x] **Step 1: 写 RED 编译与路径测试**

  证明目录将网关标为 publishable/executable；发布期拒绝字段/操作符不兼容；高金额、普通金额与缺失可选字段分别选择条件/默认路径；网关可出现在首审批前或审批后；任一路径没有审批、非网关多后继、回边、悬空和不可达仍失败关闭。

- [x] **Step 2: 运行 RED 测试**

  Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~WorkflowNodeTypeCatalogTests|FullyQualifiedName~WorkflowDefinitionCompilerTests|FullyQualifiedName~WorkflowRuntimePlanTests"`

  Expected: FAIL，因为网关仍不可用且运行计划只接受线性图。

- [x] **Step 3: 实现值感知的确定性遍历**

  编译器先校验闭合配置，再用 `nextNodeKeys` 做 DAG/可达性校验；运行计划从 start 或当前 approval 的唯一后继开始，顺序执行 CC/网关，直到下一 approval 或 end。每条到达 end 的路径必须至少经过一个 approval，并用节点数上界防御意外循环。

- [x] **Step 4: 运行 GREEN 测试**

  Run: 同 Step 2。

  Expected: PASS，既有线性审批与抄送断言保持通过。

### Task 3: 原子写入自动节点并接入启动/审批

**Files:**
- Create: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowAutomaticTransitionWriter.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowCcTransitionWriter.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageInstances/WorkflowInstanceManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyTodos/WorkflowTodoManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/WorkflowModule.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowAutomaticTransitionWriterTests.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowCcTransitionWriterTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `WorkflowApprovalTransition.AutomaticNodes`。
- Produces: `WorkflowAutomaticTransitionWriter.WriteAsync(...)`，按路径顺序写完成网关/抄送步骤和日志。

- [x] **Step 1: 写 RED 原子写入与服务编排测试**

  断言 `gateway.exclusive` 写 completed step、`node.gateway.exclusive` 日志及 `branch:<branchKey>` 摘要；自动节点顺序不被按类型重排；拒绝路径不调用自动节点写入器；审批网关读取合并后的 Field Patch。

- [x] **Step 2: 运行 RED 测试**

  Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~WorkflowAutomaticTransitionWriterTests|FullyQualifiedName~WorkflowCcTransitionWriterTests|FullyQualifiedName~WorkflowTodoManagementServiceTests"`

  Expected: FAIL，因为网关写入和新编排尚不存在。

- [x] **Step 3: 实现最小事务内编排**

  新增参数化 `InsertCompletedGatewayStep`；统一写入器按 `AutomaticNodes` 顺序调度 CC 与网关。Approve 先服务端合并/校验 Patch，再选择网关分支；Reject 只验证当前审批节点存在并进入终态，不解析下游。

- [x] **Step 4: 运行 GREEN 测试与架构测试**

  Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Workflow"`

  Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj --no-restore --filter "FullyQualifiedName~Workflow|FullyQualifiedName~Comment|FullyQualifiedName~Aot|FullyQualifiedName~Dapper"`

  Expected: PASS，且无跨模块 SQL/事务或注释门禁回归。

### Task 4: Workflow-Vue3 分支适配与受控配置抽屉

**Files:**
- Modify: `ui/admin/src/workflow/workflow-vue3-adapter.ts`
- Modify: `ui/admin/src/workflow/workflow-vue3-adapter.test.ts`
- Modify: `ui/admin/src/workflow/WorkflowVue3Designer.vue`
- Modify: `ui/admin/src/workflow/WorkflowVue3Designer.test.ts`
- Modify: `ui/admin/src/workflow/vendor/workflow-vue3/src/components/addNode.vue`
- Modify: `ui/admin/src/views/WorkflowDefinitionsView.vue`
- Modify: `ui/admin/src/views/WorkflowDefinitionsView.test.ts`
- Modify: `ui/admin/src/api/workflow-forms.ts`
- Modify: `ui/admin/src/api/workflow-forms.test.ts`

**Interfaces:**
- Consumes: 现有 `workflowGetFormVersion` 生成 Operation 与发布表单 `WorkflowFormSchema`。
- Produces: Workflow-Vue3 type `10` ↔ `gateway.exclusive` 的闭合树/IR 双向适配。
- Produces: 仅允许选择已发布表单字段、兼容操作符、规范常量和默认分支的 Element Plus 抽屉。

- [x] **Step 1: 写 RED 前端适配与交互测试**

  覆盖两分支树转换、共同后继合并、回显、字段/操作符切换、默认分支、顺序优先级、非法/远程/脚本配置拒绝，以及节点目录未启用时不显示网关入口。

- [x] **Step 2: 运行 RED Vitest**

  Run: `pnpm --filter @fullnet/admin test -- workflow-vue3-adapter.test.ts WorkflowVue3Designer.test.ts WorkflowDefinitionsView.test.ts workflow-forms.test.ts`

  Expected: FAIL，因为适配器仍拒绝 `conditionNodes` 和 type 10。

- [x] **Step 3: 实现最小 Vue 配置体验**

  从当前发布表单版本解析权威字段目录；条件分支节点只保存 `branchKey/fieldKey/operator/value`，最后一支固定为默认分支。适配器扁平化每条分支并把共同 `childNode` 编译为共享后继，拒绝非排他路由、嵌套远程条件和未知字段。

- [x] **Step 4: 运行 GREEN、类型检查与生产构建**

  Run: 同 Step 2。

  Run: `pnpm --filter @fullnet/admin typecheck`

  Run: `pnpm --filter @fullnet/admin build`

  Expected: PASS；网关代码仍随定义页面懒加载，不修改 Layui。

### Task 5: 双库运行时与拒绝路径 Integration

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Workflow/WorkflowRuntimeApiAssertions.cs`
- Modify: `eng/testing/test-matrix.json`（仅当最低发现数因新增测试确实变化）

**Interfaces:**
- Consumes: Tasks 1–4 的发布、启动、审批和轨迹行为。
- Produces: 共享 SQL Server/MySQL API 断言，不新增测试专用生产接口。

- [x] **Step 1: 写 RED Integration 场景**

  发布包含金额网关的定义，证明初始值可在首审批前选择分支、审批 Patch 可在审批后选择分支、默认路径可达、未选分支不产生步骤/待办/抄送、拒绝不执行下游网关；轨迹包含命中分支且不泄漏完整表单值。

- [x] **Step 2: 只构建 Integration 项目并确认新断言编译**

  Run: `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --no-restore`

  Expected: PASS；环境重型执行留给 Actions。

- [x] **Step 3: 审查受影响选择器**

  Run: `pnpm test:integration:affected:plan -- --snapshot workflow-gateway-exclusive-20260905 --phase slice`

  Expected: 选择 Workflow 的 SQL Server/MySQL API/Integration 与必要 AOT 门禁，不选择无关完整本地集合。

### Task 6: 合并验证、文档与 GitHub Actions

**Files:**
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/verification/2026-08-31-workflow-first-slice-closeout.md`
- Add: `docs/verification/2026-09-05-workflow-exclusive-gateway-verification.md`

**Interfaces:**
- Produces: 精确提交、命令、Actions URL、通过/失败边界和剩余 Workflow 缺口的权威记录。

- [x] **Step 1: 运行本地受影响验证**

  Run: `pnpm test:inner -- --snapshot workflow-gateway-exclusive-20260905 --plan`

  Run: 选择器输出的 Unit/Architecture/前端/OpenAPI/governance 命令。

  Run: `dotnet build Full.NET.slnx --no-restore`

  Run: `git diff --check`

- [x] **Step 2: 自审配置、运行时、前端和安全边界**

  确认无脚本/远程条件、无跨模块读取、无未绑定表单字段、无客户端信任、无 Layui 修改、无敏感值日志；检查所有后端中文 XML/关键逻辑注释。

- [x] **Step 3: 更新状态文档**

  只把 `gateway.exclusive` 首切片标为 `Build-verified`；Worker 恢复/reconcile、Notifications 投影、Tenant 本地候选、人工产品验收和生产容量继续开放。

- [ ] **Step 4: 提交、推送并验证 GitHub Actions**

  检查目标提交的 CI、SQL Server/MySQL Integration、API/Worker Linux Native AOT、客户端和真实栈。修复本切片回归；对既有 Identity/宽泛真实栈债务保留精确证据，不误标全绿。
