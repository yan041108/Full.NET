# CodeGeneration Checkpoint Retention Implementation Plan

> **Goal:** Worker 后台按 DB 资格与冷却期清理已成功回滚的本地检查点目录。

**Spec:** [`2026-08-02-codegeneration-checkpoint-retention-design.md`](../specs/2026-08-02-codegeneration-checkpoint-retention-design.md) (Approved)

**任务快照：** `codegeneration-checkpoint-retention-20260802`

### Task 1: Store delete + options RED/GREEN

- [x] RED: `TryDeleteAsync` 与 options validator 测试先行
- [x] GREEN: `GenerationRollbackCheckpointStore.TryDeleteAsync` + `CodeGenerationCheckpointRetentionOptions`

### Task 2: Runner + SQL + Worker registration

- [x] RED: Runner 资格矩阵单测
- [x] GREEN: SQL 查询、Runner、HostedProcessor、`AddBackgroundServices`

### Task 3: Verification closeout

- [x] 运行单元测试与 affected inner
- [x] 写入 verification 记录
