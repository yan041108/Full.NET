# CodeGeneration 回滚成功后删除检查点设计

**状态：** Approved for implementation
**日期：** 2026-08-02
**基线：** `main` @ `dfd876f`

## 1. 配置

`CodeGeneration:CheckpointRetention:DeleteAfterSucceededRollback`，默认 `false`。与 `Enabled`（Worker）独立。

## 2. 行为

仅在新 Rollback 写盘并 `CompleteRollback` 成功后执行 `TryDeleteAsync`；幂等重放路径不删除。删除失败记录 Warning，HTTP 仍 200。

## 3. 排除

链式回滚、迁移、双端。