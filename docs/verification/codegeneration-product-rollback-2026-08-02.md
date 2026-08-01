# CodeGeneration 产品 Rollback 验证记录

## 结论

Host 代码生成产品 Rollback 首切片已交付并保持 `Build-verified`：仅 DB 中 `apply/succeeded` 可作为资格权威，共享 `CodeGenerationApplyGate`，复用内部 `GenerationRollbackWorkspace` 与不可变检查点，写入独立 `operationKind=rollback` 运行行；Vue/Layui 在具备 `codegen.runs.rollback` 时对成功 Apply 提供确认回滚。本切片不删除检查点，不包含保留清理、多实例调度、远程 Git 或生产默认启用。

## 安全与兼容边界

- API：`POST /api/v1/code-generation/runs/rollback`，请求仅 `{ applyRunId }`；响应与历史不暴露路径、Schema、源码或异常正文。
- 权限独立于 Apply/Execute；配置仍跟随 `CodeGeneration:Apply:Enabled` 与绝对 `WorkspaceRoot`。
- 非 succeeded Apply、已成功 Rollback、检查点缺失、工作区漂移均为零工作区写入；冲突计划不写盘。
- 迁移 `051_CodeGenerationRollback` 扩展 `SourceApplyRunId` 与成功 Rollback 唯一约束，并允许 `ArtifactCount >= 0`。
- 原 Apply 行保持 `succeeded` 只读证据。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| HEAD 序列 | Task1 `744e197` → Task2 `6516e9c` → Task3 `beabad5` → Task4 `44729a4` → Task5 closeout |
| `pnpm test:naming` | 24/24（Task 1） |
| Migration051 双库恢复 | 2/2 |
| Rollback Unit（服务/端点权限/注册） | 9/9 |
| CodeGeneration API SQL Server/MySQL runs 合同（含 Rollback） | 2/2 |
| `@fullnet/client-contracts` | 105/105 |
| Vue 聚焦 `code-generation-runs` + PreviewsView | 10/10 |
| Layui 聚焦 runs + previews | 5/5 |
| real-stack bootstrap contracts（含 Rollback 路径门禁） | 11/11 |
| Vue/Layui Host Apply→Rollback 真实浏览器 E2E | 4/4 |

## 真实栈 E2E 状态

`host-code-generation-templates` 在干净真实栈上 Vue/Layui **4/4** 通过：成功 Apply 后确认回滚、工作区 Manifest 清空为 `artifacts=[]`、受限 Host 账号 Rollback 返回 `403 authorization.permission_denied`。总体 CodeGeneration 能力仍保持 `Build-verified`（未标 `Verified`）：检查点保留清理已交付（见 [codegeneration-checkpoint-retention-2026-08-02.md](codegeneration-checkpoint-retention-2026-08-02.md)）；多实例工作区互斥已交付（见 [codegeneration-distributed-workspace-gate-2026-08-02.md](codegeneration-distributed-workspace-gate-2026-08-02.md)）；重复 Rollback 幂等已交付（见 [codegeneration-rollback-idempotency-2026-08-02.md](codegeneration-rollback-idempotency-2026-08-02.md)）；检查点容量配额 `MaxCheckpointCount` 已交付（见 [codegeneration-checkpoint-max-count-2026-08-02.md](codegeneration-checkpoint-max-count-2026-08-02.md)）；远程 Git 工作区同步已交付（见 [codegeneration-remote-git-2026-08-02.md](codegeneration-remote-git-2026-08-02.md)）；回滚后删除检查点已交付（见 [codegeneration-rollback-checkpoint-delete-2026-08-02.md](codegeneration-rollback-checkpoint-delete-2026-08-02.md)）；生产默认启用未交付。

## 未交付

链式多 Apply 回滚、默认生产启用。

## 治理复盘

未命中规则或 Skill 升级触发条件；一行结论：无需规则/Skill 变更。

