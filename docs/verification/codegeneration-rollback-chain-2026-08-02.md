# CodeGeneration 链式多 Apply 回滚验证记录

## 结论

`POST /api/v1/code-generation/runs/rollback-chain` 已交付并保持 `Build-verified`：请求 `{ applyRunIds }` 须为同一实体待回滚栈的 LIFO 前缀（2..`MaxRollbackChainLength`，默认 16）；整链一次 Gate、一次 Git sync、逐步复用单 Apply 回滚内核；权限复用 `codegen.runs.rollback`。双端 UI 已交付（见 [codegeneration-rollback-chain-ui-2026-08-02.md](codegeneration-rollback-chain-ui-2026-08-02.md)）。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| Rollback 单元 + 端点安全 | 15/15 |
| Integration affected（链式窗口） | 32/32 |
| `@fullnet/client-contracts` | 108/108 |
| real-stack E2E（双 Apply→链式回滚） | spec `4de510d` |

## 治理复盘

未命中规则或 Skill 升级触发条件；一行结论：无需规则/Skill 变更。