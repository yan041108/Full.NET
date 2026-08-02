# CodeGeneration OrganizationUnit Ownership Implementation Plan

> **For agentic workers:** Use `fullnet-module-delivery`. RED first.

**Goal:** 交付组织归属写入授权端口并解除 OrganizationUnit 生成 fail-closed，附双库运行时矩阵。

**Spec:** [`docs/superpowers/specs/2026-08-02-codegeneration-organization-ownership-design.md`](../specs/2026-08-02-codegeneration-organization-ownership-design.md)（Approved）

**Snapshot:** `codegeneration-organization-ownership-20260802`

### Task 1: Write authorization port (Organization)

- [x] **Step 1:** RED — `IOrganizationOwnedEntityWriteAuthorizer` + Organization 实现 + Unit 测试。
- [x] **Step 2:** GREEN — 隶属/数据范围拒绝与成功路径。

### Task 2: Generator + runtime matrix

- [x] **Step 1:** RED — 解除 fail-closed；生成 Feature/SQL 片段测试；Integration 组织归属 SoftDelete 矩阵。
- [x] **Step 2:** GREEN — Unit + 双库 Integration。

### Task 3: Closeout

- [x] 验证记录；评估状态关闭；test-matrix 更新。

### Task 4: Post-closeout supplements（Build-verified 加固）

- [x] 模块编译集成（`fca4554`）
- [x] Host/Global 互斥 + CLI/Preview API（`246613a`–`e7db0ed`）
- [x] Tree/关系 fail-closed 回归（`cfae6d8`）
- [x] E2E Schema 助手 + spec-contracts（`848c20f`–`1f03ccf`）
- [x] Runs 跟踪预览 Integration（`494cd3f`）
- [ ] `Verified`：双库 SQL 运行时 + 真实栈 E2E（需 Docker/真实栈环境）