# CodeGeneration 回滚后删除检查点验证记录

## 结论

产品 Rollback 成功后可选删除对应检查点目录已交付并保持 `Build-verified`：配置 `CodeGeneration:CheckpointRetention:DeleteAfterSucceededRollback`（默认 `false`）在新 Rollback 收敛后调用 `GenerationRollbackCheckpointStore.TryDeleteAsync`；幂等重放不触发删除；删除失败仅记 Warning，HTTP 仍 200。

## 配置

- `CodeGeneration:CheckpointRetention:DeleteAfterSucceededRollback`：与 Worker `Enabled` 独立，默认关闭。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| `CodeGenerationRollbackServiceTests` | 9/9 |
| `pnpm test:integration:affected --snapshot codegeneration-rollback-checkpoint-delete-20260802 --phase inner` | 32/32 |

## 治理复盘

未命中规则或 Skill 升级触发条件；一行结论：无需规则/Skill 变更。