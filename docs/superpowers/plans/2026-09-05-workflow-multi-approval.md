# Workflow Multi-Approval Implementation Plan

**状态：** Build-verified；页面真实栈 E2E、视觉调整、人工逐页验收及双库完整 API 分片按用户与仓库规则后置。

**批准依据：** [`2026-08-20-workflow-module-design.md`](../specs/2026-08-20-workflow-module-design.md) 与当前用户对编号 5 的继续执行授权。

**Goal:** 在同一人工审批步骤中交付会签 `all`、或签 `any` 和法定票数 `nOfM`，并同时完成定义配置、待办办理和 Vue 进度呈现。

**Architecture:** 不可变定义版本在 `human.approval.config.approvalPolicy` 中固化办理人快照策略；节点激活时创建一个步骤、多个 Approval Slot 和一人一条 Todo。Slot 是投票权威事实，步骤上的模式与票数仅是激活快照；每次表决通过提交快照、Todo、Slot、Step 和 Instance 的乐观锁在 Workflow 本地事务内串行裁决，终态后统一取消剩余工作。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper 显式 SQL、DbUp、SQL Server/MySQL、System.Text.Json 源生成、Vue 3、TypeScript、Element Plus、Vitest、OpenAPI 生成客户端。

## Global Constraints

- 本编号只产生一个独立提交，完成后停止，不提前实现转办或加签。
- 只修改 Vue 主管理端 `ui/admin`；`ui/admin-layui` 保持零改动。
- 页面真实栈 E2E、视觉微调与人工逐页验收延后，状态最高为 `Build-verified`。
- 配置办理人必须来自可信 Host/Tenant 活动用户目录；请求和 JSON 中不得接受租户标识。
- `all` 要求全部同意，`any` 要求一票同意，`nOfM` 要求 `1 < N < M`；拒绝票使剩余最大同意数小于 N 时节点拒绝。
- 每个用户在同一步骤只有一个 Slot 和一个 Todo；节点终态提交后剩余 Slot/Todo 一次性取消。
- 数据库变更必须提供 113 SQL Server/MySQL 成对迁移、部分 DDL 恢复测试、对象注释和命名治理登记。

---

### Task 1: 冻结审批策略与 RED

**Files:**
- Create: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowApprovalPolicy.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowApprovalPolicyTests.cs`
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowRuntimePlanTests.cs`
- Modify: `tests/Full.NET.UnitTests/Workflow/WorkflowDefinitionCompilerTests.cs`

**Interfaces:**
- Produces: `WorkflowApprovalPolicy.TryRead(JsonElement, out WorkflowApprovalPolicy?)`，以及运行迁移携带的模式、办理人和法定票数。

- [ ] 先写失败测试，覆盖三种模式、重复/空办理人、非法 N、未知字段和缺省单人兼容。
- [ ] 运行聚焦测试，确认因策略类型或迁移属性不存在而 RED。
- [ ] 实现最小严格解析与 Runtime Plan 传播，并让编译器拒绝非法配置。
- [ ] 复跑聚焦测试至 GREEN。

### Task 2: 双库 Slot 权威事实与激活写入

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/113_WorkflowMultiApproval.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/113_WorkflowMultiApproval.sql`
- Create: `src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowApprovalActivationWriter.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowRecords.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowDapperAotMaterializerContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/WorkflowModule.cs`
- Test: `tests/Full.NET.IntegrationTests/Migrations/Migration113WorkflowMultiApprovalRecoveryTests.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowApprovalActivationWriterTests.cs`

**Interfaces:**
- Produces: `WorkflowApprovalActivationWriter.WriteAsync(...)`，原子创建一个步骤、M 个 Slot/Todo，并返回首个 Todo 与全部通知目标。

- [ ] 先写 Writer 和迁移恢复失败测试，证明 Slot 唯一性、步骤快照及部分 DDL 恢复要求。
- [ ] 实现成对迁移、SQL Statement、AOT 物化与 Writer。
- [ ] 运行聚焦 Unit；数据库测试在环境可用时执行两库，否则明确交给 CI。

### Task 3: 发布校验、启动与后续节点激活

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/WorkflowDefinitionManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageInstances/WorkflowInstanceManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyTodos/WorkflowTodoManagementService.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowDefinitionManagementServiceTests.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowInstanceManagementServiceTests.cs`

**Interfaces:**
- Consumes: 发布版本审批策略与 `WorkflowApprovalActivationWriter`。
- Produces: 发布时批量活动用户校验；启动和上一步完成时创建同一节点的一人一 Todo。

- [ ] 写失败测试，覆盖跨作用域/停用办理人拒绝、多人首节点和多人后继节点激活。
- [ ] 批量复用现有 Identity 最小目录，并保证目录调用不进入 Workflow 本地事务。
- [ ] 用 Activation Writer 替换单 Todo 激活逻辑，保留无策略定义的发起人/当前办理人兼容语义。
- [ ] 复跑测试至 GREEN。

### Task 4: 并发投票与确定终态

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyTodos/WorkflowTodoManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Features/ManageMyTodos/Contracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowSql.cs`
- Test: `tests/Full.NET.UnitTests/Workflow/WorkflowMultiApprovalServiceTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Workflow/WorkflowRuntimeApiAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/WorkflowApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/WorkflowApiMySqlTests.cs`

**Interfaces:**
- Produces: 现有 approve/reject API 的多人审批语义，以及 Todo Detail 中 `approvalModeKey/required/approved/rejected/pending` 进度。

- [ ] 写失败测试覆盖 ALL 等待全部、ANY 首票终态、N-of-M 达标、拒绝后不可能达标、重复/并发表决及终态取消。
- [ ] 以 Slot 决策 + Step Revision CAS 串行裁决；失败 Result 必须回滚此前 Todo/Slot/提交写入。
- [ ] 节点未终态时只推进 Instance Revision；终态时关闭步骤、取消剩余工作并按既有运行计划推进。
- [ ] 增加双库 API 持久化断言并复跑本地可执行测试。

### Task 5: Vue 定义配置与待办进度

**Files:**
- Modify: `ui/admin/src/workflow/WorkflowVue3Designer.vue`
- Modify: `ui/admin/src/workflow/workflow-vue3-adapter.ts`
- Modify: `ui/admin/src/workflow/WorkflowVue3Designer.test.ts`
- Modify: `ui/admin/src/workflow/workflow-vue3-adapter.test.ts`
- Modify: `ui/admin/src/views/WorkflowTodosView.vue`
- Modify: `ui/admin/src/views/WorkflowTodosView.test.ts`
- Modify: `packages/admin-i18n/src/messages.ts`

**Interfaces:**
- Consumes: 活动收件人目录和 Todo Detail 多人审批进度。
- Produces: 审批抽屉中的模式、办理人和 N 配置，以及待办抽屉中的实时票数摘要。

- [ ] 先写 Vitest RED，覆盖保存/回显三种策略、非法 N 阻断和进度显示。
- [ ] 实现 Element Plus 配置控件与稳定草稿转换，不新增第三方依赖。
- [ ] 复跑聚焦 Vitest 和类型检查至 GREEN。

### Task 6: OpenAPI、文档与提交门禁

**Files:**
- Modify: `contracts/openapi/fullnet-client-v1.openapi.json`
- Modify: `packages/client-contracts/src/generated/*.generated.ts`
- Modify: `contracts/architecture/global-sql-statements.json`
- Modify: `contracts/database/object-comments.json`
- Modify: `contracts/naming/naming-debt.json`
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/superpowers/specs/2026-08-20-workflow-module-design.md`

**Interfaces:**
- Produces: 与运行时一致的生成客户端和 `Build-verified` 验证记录。

- [ ] 刷新双 Provider OpenAPI 快照并生成客户端，验证零漂移。
- [ ] 更新唯一测试矩阵、SQL/对象注释治理和 Workflow 规格；普通测试结果保留在提交交付说明，不额外创建 Verification。
- [ ] 运行 Release 构建、聚焦/全量 Unit、Architecture、OpenAPI、命名、SQL 安全、治理、AOT 分析、Vue 测试/类型检查及受影响计划。
- [ ] 检查 `git diff --check`、分支、工作区和 Layui 零改动；只创建一个编号 5 提交后停止。
