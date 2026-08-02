# CodeGeneration 产品 Rollback 验证记录

## 结论

Host 代码生成产品 Rollback 演进切片已全部交付并保持 `Build-verified`：以 DB 中 `apply/succeeded` 为资格权威，共享 `CodeGenerationApplyGate`（含可选分布式互斥），复用内部 `GenerationRollbackWorkspace` 与不可变检查点，写入独立 `operationKind=rollback` 运行行；支持单次 `rollback` 与 LIFO `rollback-chain`；Vue/Layui 在具备 `codegen.runs.rollback` 时对成功 Apply 提供确认回滚（非栈顶自动链式）。检查点保留/容量、重复回滚幂等、远程 Git 同步、回滚后删除检查点、生产 Helm 默认启用路径均已按独立切片交付。总体能力在完整 `main` CI 与更广真实栈矩阵通过前不标 `Verified`。

## 安全与兼容边界

- API：`POST /api/v1/code-generation/runs/rollback`（`{ applyRunId }`）与 `POST /api/v1/code-generation/runs/rollback-chain`（`{ applyRunIds }`，LIFO 前缀 2..`MaxRollbackChainLength`）；响应与历史不暴露路径、Schema、源码或异常正文。
- 权限独立于 Apply/Execute，复用 `codegen.runs.rollback`；配置仍跟随 `CodeGeneration:Apply:Enabled` 与绝对 `WorkspaceRoot`。
- 非 succeeded Apply、已成功 Rollback、检查点缺失、工作区漂移均为零工作区写入；冲突计划不写盘。
- 迁移 `051_CodeGenerationRollback` 扩展 `SourceApplyRunId` 与成功 Rollback 唯一约束，并允许 `ArtifactCount >= 0`。
- 原 Apply 行保持 `succeeded` 只读证据。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| 演进闭合 HEAD | `4de510d`（含 rollback-chain API/UI/E2E） |
| `CodeGenerationRollbackServiceTests` + 端点权限/注册 | 15/15 |
| `@fullnet/client-contracts` | 108/108 |
| Vue 聚焦 code-generation（含 PreviewsView 链式确认） | 22/22 |
| Layui 聚焦 code-generation | 8/8 |
| real-stack spec-contracts（含 `rollback-chain` 路径） | 11/11 |
| 演进切片 integration affected（链式/API 窗口） | 32/32 |

## 真实栈 E2E 状态

`host-code-generation-templates`：单次 Apply→Rollback、受限账号 `403`、双次 Apply 后对较旧 Apply 触发 `rollback-chain`（含 `rollback-chain` 403 用例）已在 spec 与 contracts 门禁覆盖；完整 `pnpm test:e2e:real` 依赖本地真实栈，本机未在本轮重复执行。

演进切片索引：检查点保留（[codegeneration-checkpoint-retention-2026-08-02.md](codegeneration-checkpoint-retention-2026-08-02.md)）、多实例互斥（[codegeneration-distributed-workspace-gate-2026-08-02.md](codegeneration-distributed-workspace-gate-2026-08-02.md)）、幂等（[codegeneration-rollback-idempotency-2026-08-02.md](codegeneration-rollback-idempotency-2026-08-02.md)）、`MaxCheckpointCount`（[codegeneration-checkpoint-max-count-2026-08-02.md](codegeneration-checkpoint-max-count-2026-08-02.md)）、远程 Git（[codegeneration-remote-git-2026-08-02.md](codegeneration-remote-git-2026-08-02.md)）、回滚后删检查点（[codegeneration-rollback-checkpoint-delete-2026-08-02.md](codegeneration-rollback-checkpoint-delete-2026-08-02.md)）、链式 API（[codegeneration-rollback-chain-2026-08-02.md](codegeneration-rollback-chain-2026-08-02.md)）、生产 Helm（[codegeneration-production-helm-enable-2026-08-02.md](codegeneration-production-helm-enable-2026-08-02.md)）、双端 UI（[codegeneration-rollback-chain-ui-2026-08-02.md](codegeneration-rollback-chain-ui-2026-08-02.md)）。

## 未交付

（无 — CodeGeneration 产品 Rollback 演进切片已闭合；能力仍保持 `Build-verified`。）

## 治理复盘

未命中规则或 Skill 升级触发条件；一行结论：无需规则/Skill 变更。