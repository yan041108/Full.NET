# CodeGeneration 双端 Rollback Chain UI 验证记录

## 结论

Vue/Layui 预览工作台已接入 `rollback-chain`：对同一实体待回滚栈上的非栈顶 Apply 自动按 LIFO 前缀调用链式 API；栈顶仍走单次 `rollback`。已回滚 Apply 不再显示回滚按钮。能力仍保持 `Build-verified`（真实栈 Vue/Layui 链式回滚 E2E 已覆盖；总体 `Verified` 仍待全量门禁）。

## 交付

- `@fullnet/client-contracts`：`buildCodeGenerationRollbackApplyRunIds` / `isPendingCodeGenerationRollbackApply`
- Vue：`executeTrackedCodeGenerationRollback` + 确认文案 `rollbackChainConfirm`
- Layui：`rollbackApply` / `rollbackChain` API 与同等确认流
- i18n：中英 `codeGeneration.rollbackChainConfirm`

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| `@fullnet/client-contracts` | 108/108 |
| Vue `code-generation-runs` + `CodeGenerationPreviewsView` | 11/11 |
| Layui `code-generation-runs` | 2/2 |
| real-stack spec-contracts | 含 `rollback-chain` 路径断言 |

## 治理复盘

未命中规则或 Skill 升级触发条件；一行结论：无需规则/Skill 变更。