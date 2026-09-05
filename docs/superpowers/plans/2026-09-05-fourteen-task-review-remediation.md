# Fourteen Task Review Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 14 个累计任务合并后暴露的业务一致性、模块边界、契约生成、治理目录和 Vue 编译回归，使非页面验收门禁恢复为绿色。

**Architecture:** DataApproval 只能通过稳定 Contracts 使用 SerialNumbers，并且本地事务不得包裹 Workflow 跨模块写入。审批应用发生版本冲突时失败关闭，只有能够证明目标状态已等于批准快照时才允许幂等成功。OpenAPI、JSON 源生成、数据库对象、SQL 和模块目录以现有生成器及精确登记文件为唯一事实源。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper、SQL Server/MySQL DbUp、MemoryPack/System.Text.Json 源生成、Vue 3、TypeScript、Vitest、pnpm。

## Global Constraints

- 保持强化型模块化单体；跨模块只引用 `*.Contracts`，本地事务只写当前模块表。
- SQL Server 与 MySQL 的迁移、注释和恢复测试必须成对维护。
- API 使用标准状态码与 ProblemDetails；JSON 进入生成客户端前必须具有运行时守卫。
- 不修改页面视觉，不运行页面级真实栈 E2E，不执行人工逐页验收。
- 所有后端新增或修改成员保留中文 XML 文档注释和参数说明。

---

### Task 1: 修复审批一致性和模块边界

**Files:**
- Modify: `src/Modules/Full.NET.Modules.DataApproval/Features/ManageRequests/DataApprovalRequestService.cs`
- Modify: `src/Modules/Full.NET.Modules.DataApproval/Features/ProjectWorkflowOutcomes/DataApprovalWorkflowOutcomeService.cs`
- Modify: `src/Modules/Full.NET.Modules.SerialNumbers/Features/DataApprovalBridge/SerialRuleChangeApprovalApplier.cs`
- Modify/Create: SerialNumbers 稳定 Contracts 项目、解决方案与模块引用文件
- Test: `tests/Full.NET.UnitTests/DataApproval/**`
- Test: `tests/Full.NET.ArchitectureTests/**`

**Interfaces:**
- Consumes: `IWorkflowInstanceStarter`、`ISerialRuleChangeApprovalSource`、`ISerialRuleChangeApprovalApplier`。
- Produces: 失败关闭的审批应用结果，以及不引用 SerialNumbers 实现程序集的 DataApproval 项目。

- [ ] 增加版本冲突不得把请求标为 approved 的失败测试。
- [ ] 增加 DataApproval 不引用 SerialNumbers 实现程序集、事务内不调用 Workflow Contract 的架构测试。
- [ ] 将 SerialNumbers 跨模块契约隔离到稳定 Contracts 程序集。
- [ ] 将 Workflow 启动移出 DataApproval 本地事务，并保留可恢复、可重放状态。
- [ ] 运行 DataApproval/SerialNumbers 聚焦 Unit 与 Architecture 测试。

### Task 2: 修复 Vue 运行时、类型和契约适配

**Files:**
- Modify: `ui/admin/src/views/WorkflowInstancesView.vue`
- Modify: `ui/admin/src/api/data-approval-requests.ts`
- Modify: `ui/admin/src/api/workflow-todos.ts`
- Modify: `ui/admin/src/api/workflow-definitions.ts`
- Modify: 对应 Vitest 夹具与生成客户端调用

**Interfaces:**
- Consumes: `@fullnet/client-contracts` 生成 Operation 与运行时守卫。
- Produces: 无手写后端形状、无 `request<T>` 绕过守卫且可通过类型检查的薄适配层。

- [ ] 复现 9 个 Vitest 失败和 10 个 TypeScript 错误。
- [ ] 补齐 `timeoutStatusKeys` 并修复页面单测。
- [ ] 将新增 API 改为生成 Operation 或 `unknown` 加运行时守卫。
- [ ] 更新已演进响应的测试夹具，移除重复类型导出和 readonly 不兼容。
- [ ] 运行 Admin Vitest、typecheck 和 client-contracts 测试。

### Task 3: 收敛生成物和治理目录

**Files:**
- Modify: `contracts/openapi/**`
- Modify: `packages/client-contracts/src/generated/**`
- Modify: `contracts/architecture/**`
- Modify: `contracts/database/object-comments.json`
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/operations/module-dependency-graph.mmd`
- Modify: `src/Hosts/Full.NET.Host.Api/Serialization/**` 及相关模块 JSON 上下文

**Interfaces:**
- Consumes: 当前 C# Endpoint、模块依赖、SQL Statement 和迁移集合。
- Produces: 确定性的 OpenAPI 客户端、模块图、对象注释与精确影响集登记。

- [ ] 更新运行时 OpenAPI 快照并重新生成客户端，确认二次生成零漂移。
- [ ] 登记新增模块、表归属、29 条 Global SQL、迁移 114–118 与恢复选择器。
- [ ] 为 Workflow 候选分页响应补齐 JSON 源生成元数据。
- [ ] 生成模块依赖图并清理验证文档行尾空格。
- [ ] 运行 OpenAPI、Naming、Governance、SQL Safety 和 `git diff --check`。

### Task 4: 合并级回归验证与交付

**Files:**
- Modify: 仅修改被新鲜验证结果真实影响的验证记录和测试矩阵。

**Interfaces:**
- Consumes: Tasks 1–3 的最终代码和生成物。
- Produces: 可提交、可合并且工作区干净的修复分支。

- [ ] 运行完整 Unit、Architecture、Vue test/typecheck 及非容器治理门禁。
- [ ] 使用任务快照规划 merge 影响集；环境重型双库、Native AOT 和页面真实栈交给目标提交 GitHub Actions。
- [ ] 检查 `git diff --check`、`git status` 和分支状态。
- [ ] 提交修复，合并回 `main`，删除修复分支并确认 `main` 工作区干净。
