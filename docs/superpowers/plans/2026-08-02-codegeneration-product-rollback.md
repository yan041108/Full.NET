# CodeGeneration Product Rollback Implementation Plan

> **For agentic workers:** Use `fullnet-module-delivery`. Execute task-by-task with RED first. Steps use `- [x]` checkboxes.

**Goal:** 以 DB `apply/succeeded` 为资格权威，交付 Host 产品 Rollback HTTP/权限/051 双库运行态，并复用内部 `GenerationRollbackWorkspace`，同步 Vue/Layui。

**Architecture:** `CodeGenerationRollbackService` 在共享 `CodeGenerationApplyGate` 内校验资格、读取检查点、调用 `RestoreAsync`，并把结果写入 `fn_codegeneration_run` 的 `operationKind=rollback` 行；原 Apply 行保持只读证据。

**Tech Stack:** .NET 10、Dapper、DbUp SQL Server/MySQL、System.Text.Json、Vue、Layui、MSTest、真实栈 E2E。

**Spec：** [`docs/superpowers/specs/2026-08-02-codegeneration-product-rollback-design.md`](../specs/2026-08-02-codegeneration-product-rollback-design.md)（Approved）

## Global Constraints

- 任务快照：`codegeneration-product-rollback-20260802`；基线 HEAD 以任务开始时 `git rev-parse HEAD` 为准。
- 不删除检查点；不扩大到保留清理/多实例/远程 Git。
- 迁移编号固定 **051**；公共 API 不暴露路径/源码。
- 双端未齐备前不得标 `Verified`；整体能力保持 `Build-verified` 直至本计划关闭证据写入 verification。
- 工作区已脏时使用任务快照；只改 CodeGeneration / 051 / client-contracts / 双端相关文件。

---

### Task 1: Contracts, permission and 051 migration RED/GREEN

**Files:**
- Modify: `Contracts/CodeGenerationRunContracts.cs`
- Modify: `CodeGenerationAuthorizationContributor.cs`
- Create: `Migrations/SqlServer/051_CodeGenerationRollback.sql`
- Create: `Migrations/MySql/051_CodeGenerationRollback.sql`
- Create: `tests/.../Migration051CodeGenerationRollbackRecoveryTests.cs`
- Modify: Persistence SQL/record for `SourceApplyRunId`

- [x] **Step 1:** RED — 权限常量、`rollback` operationKind、错误码与 051 恢复测试因缺失而失败。
- [x] **Step 2:** 实现契约、授权贡献、双库 051（含成功 Rollback 唯一约束与 `ArtifactCount >= 0` 成功语义）。
- [x] **Step 3:** naming + 051 双库恢复 GREEN。

### Task 2: Rollback orchestration service

**Files:**
- Create: `Features/ManageHostRuns/CodeGenerationRollbackService.cs`
- Modify: `Endpoint.cs`, `CodeGenerationModule.cs`, JSON context
- Create/Modify: Unit tests mirroring Apply service patterns

- [x] **Step 1:** RED — 资格拒绝、busy、checkpoint 缺失、成功恢复摘要。
- [x] **Step 2:** 实现服务：共享 Gate → insert running → Read/Restore → complete/fail。
- [x] **Step 3:** Unit GREEN；冲突计划零写入。

### Task 3: Integration API matrix and OpenAPI

**Files:**
- Modify: `CodeGenerationRunAssertions.cs`（或新建 Rollback assertions）
- Modify: `contracts/openapi/code-generation-runs-v1.json`
- Architecture endpoint permission coverage

- [x] **Step 1:** RED — 403/资格/成功 Rollback 双 Provider。
- [x] **Step 2:** 接线 Endpoint 与 OpenAPI。
- [x] **Step 3:** affected inner CodeGeneration GREEN。

### Task 4: client-contracts + Vue/Layui + real-stack E2E

**Files:**
- Modify: `packages/client-contracts/src/code-generation-runs.ts`
- Modify: Vue/Layui API 与运行详情/预览相关视图
- Modify/Create: real-stack E2E spec

- [x] **Step 1:** RED — 契约与双端权限可见性。
- [x] **Step 2:** 确认对话框与错误码处理。
- [x] **Step 3:** 真实栈 Apply→Rollback E2E GREEN（SQL Server；MySQL 按现有矩阵）。

### Task 5: Closeout

**Files:**
- Create: `docs/verification/codegeneration-product-rollback-2026-08-02.md`
- Modify: `capability-status.md`, `adminnet-feature-parity.md`, assessment 状态
- Modify: `eng/testing/test-matrix.json` only after fresh discovery

- [x] **Step 1:** affected slice + naming + `git diff --check`。
- [x] **Step 2:** 记录证据；明确未交付保留清理/多实例。
- [x] **Step 3:** 规则/Skill 演进一行结论；本计划勾选。

## Acceptance Checklist

- [x] DB succeeded Apply 是唯一资格权威。
- [x] 共享 Gate；复用 RollbackWorkspace；不删检查点。
- [x] 051 双库恢复；独立 `codegen.runs.rollback`。
- [x] Vue + Layui + 真实栈 E2E。
- [x] 文档不把内部执行器宣传为完整 Verified Rollback。