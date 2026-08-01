# CodeGeneration 回滚检查点保留与清理首切片设计

**状态：** Approved for implementation
**日期：** 2026-08-02
**基线：** `main` @ `839b7b9`
**上游建议稿：** [codegeneration-rollback-checkpoint-retention-assessment-2026-08-02.md](../../verification/codegeneration-rollback-checkpoint-retention-assessment-2026-08-02.md)
**适用范围：** Host 代码生成 Apply 工作区、本地回滚检查点目录、Worker 后台扫描

## 1. 决策摘要

产品 Rollback 首切片不删除检查点。本设计在 **Worker 后台**、**opt-in**、**DB 资格 + 工作区安全校验**下清理已成功回滚且过冷却期的检查点目录，避免磁盘无限增长。不扩展 HTTP、双端 UI、数据库表或多实例调度。

用户未确认的未决项采用建议默认：`RetentionDays` 冷却期、仅 succeeded Rollback 可清理、仅 Worker 执行、仅日志不扩表。

## 2. 配置

配置节 `CodeGeneration:CheckpointRetention`：

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `Enabled` | `false` | 生产默认关闭 |
| `RetentionDays` | `7` | 成功 Rollback 终态后至少经过的天数 |
| `PollSeconds` | `3600` | Worker 轮询间隔 |
| `MaxDeletesPerRun` | `20` | 单次扫描上限 |

执行前必须同时满足：`CodeGeneration:Apply:Enabled = true` 且 `WorkspaceRoot` 为已存在的本地绝对路径。

## 3. 清理资格

仅当以下全部成立时才可删除 `{WorkspaceRoot}/.fullnet/codegeneration-rollback-checkpoints/{applyRunId:N}`：

1. `fn_codegeneration_run` 存在 `Id = applyRunId AND operationKind = apply AND status = succeeded`。
2. 存在 `operationKind = rollback AND status = succeeded AND SourceApplyRunId = applyRunId`。
3. 成功 Rollback 行的 `FinishedAtUtc <= UtcNow - RetentionDays`。
4. 检查点目录存在且 `ReadAsync` 可解析；损坏则跳过并记录失败计数。
5. 工作区当前 Manifest 与检查点 `PreviousManifest` 语义一致（均为空视为一致）；不一致则跳过（保留证据）。

未发起成功 Rollback 的 Apply 检查点永不自动清理。

## 4. 执行与幂等

- `CodeGenerationCheckpointRetentionRunner` 查询资格列表→逐个安全校验→ `GenerationRollbackCheckpointStore.TryDeleteAsync`。
- 目录不存在视为已清理（跳过）。
- 删除前复验 `checkpoint.json` 摘要；删除失败保留目录下轮重试。
- `CodeGenerationCheckpointRetentionHostedProcessor` 仅在 `CodeGenerationModule.AddBackgroundServices` 注册；API 进程不启动。

## 5. 观测

结构化日志计数：已扫描、已删除、已跳过、失败。不记录绝对路径、源码或异常正文。

## 6. 验收

- Unit：资格矩阵、Manifest 不一致跳过、`TryDeleteAsync` 幂等、配置验证。
- 不标记产品 Rollback `Verified`；本切片独立 verification 记录。
- 无需双端或新迁移。
