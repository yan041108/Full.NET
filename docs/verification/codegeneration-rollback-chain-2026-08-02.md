# CodeGeneration 链式多 Apply 回滚验证记录

## 结论

`POST /api/v1/code-generation/runs/rollback-chain` 已交付并保持 `Build-verified`：请求 `{ applyRunIds }` 须为同一实体待回滚栈的 LIFO 前缀（2..`MaxRollbackChainLength`，默认 16）；整链一次 Gate、一次 Git sync、逐步复用单 Apply 回滚内核；权限复用 `codegen.runs.rollback`。双端 UI 未纳入本切片。

## 配置

- `CodeGeneration:Apply:MaxRollbackChainLength`：默认 `16`，范围 `2..64`（Apply 启用时校验）。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| `CodeGenerationRollbackServiceTests` + endpoint security | 15/15 |
| `@fullnet/client-contracts` | 106/106 |
| `pnpm test:integration:affected --snapshot codegeneration-rollback-chain-20260802 --phase inner` | 32/32 |

## 治理复盘

未命中规则或 Skill 升级触发条件；一行结论：无需规则/Skill 变更。