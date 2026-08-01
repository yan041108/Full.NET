# CodeGeneration 检查点 MaxCheckpointCount 设计

**状态：** Approved for implementation
**日期：** 2026-08-02
**基线：** `main` @ `8362c28`

## 1. 配置

`CodeGeneration:CheckpointRetention:MaxCheckpointCount`，默认 `0`（禁用）。`>0` 时统计 `{WorkspaceRoot}/.fullnet/codegeneration-rollback-checkpoints` 子目录数；若超过上限，在单次扫描剩余配额内删除资格满足的最旧成功 Rollback 检查点（**可不满足 RetentionDays**）。

## 2. 扫描顺序

1. 先执行既有 `RetentionDays` 清理；
2. 再重新计数；若仍 `> MaxCheckpointCount`，查询无冷却期资格列表（`FinishedAtUtc ASC`）继续删除至不超限或配额用尽。

资格、Manifest 校验、`TryDeleteAsync` 与首切片相同。未成功 Rollback 的检查点永不因容量删除。

## 3. 排除

远程 Git、生产默认启用、链式回滚。