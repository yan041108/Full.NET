# CodeGeneration Lifecycle Runtime SQL Matrix Implementation Plan

> **For agentic workers:** Use `fullnet-module-delivery`. RED first.

**Goal:** 双库执行生成 SoftDelete 生命周期 SQL 矩阵，闭合 Admin.NET 生命周期吸收的运行时证据缺口。

**Architecture:** Integration 测试从 `CrudArtifactGenerator` 读取迁移模板与 `ProductSql` 常量，在 Testcontainers 隔离库建表并跑 Create→Update→SoftDelete 序列。

**Spec:** [`docs/superpowers/specs/2026-08-02-codegeneration-lifecycle-runtime-sql-design.md`](../specs/2026-08-02-codegeneration-lifecycle-runtime-sql-design.md)（Approved）

**Snapshot:** `codegeneration-lifecycle-runtime-sql-20260802`

### Task 1: RED integration matrix

**Files:**
- Create: `tests/Full.NET.IntegrationTests/CodeGeneration/LifecycleRuntimeSqlTestSupport.cs`
- Create: `tests/Full.NET.IntegrationTests/CodeGeneration/GeneratedLifecycleSqlRuntimeIntegrationTests.cs`

- [x] **Step 1:** RED — 双库矩阵测试（可先因缺失 helper 编译失败）。
- [x] **Step 2:** 实现 fixture + SQL 常量解析 + Dapper 执行。
- [x] **Step 3:** 双库 GREEN（编译通过；运行时依赖 Testcontainers）。

### Task 2: Closeout

**Files:**
- Create: `docs/verification/codegeneration-lifecycle-runtime-sql-2026-08-02.md`
- Modify: `docs/verification/codegeneration-adminnet-lifecycle-2026-07-30.md`（状态/链接）
- Modify: `eng/testing/test-matrix.json`（仅 fresh discovery 后）

- [x] **Step 1:** Release 编译 + test-matrix +2。
- [x] **Step 2:** 验证记录与评估状态关闭。