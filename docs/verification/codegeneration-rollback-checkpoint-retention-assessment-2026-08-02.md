# CodeGeneration 回滚检查点保留与清理评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `2ecdec8`
- 状态：**建议稿**（待用户确认边界后进入 Spec）
- 上游证据：[产品 Rollback 验证记录](codegeneration-product-rollback-2026-08-02.md)、[Apply 检查点验证](codegeneration-apply-rollback-checkpoint-2026-08-01.md)、[内部逆向执行验证](codegeneration-rollback-workspace-2026-08-01.md)

## 1. 结论

产品 Rollback 首切片刻意**不删除** `GenerationRollbackCheckpointStore` 证据目录，工作区会在多次 Apply 后累积 `{WorkspaceRoot}/.fullnet/codegeneration-rollback-checkpoints/{applyRunId:N}`。下一合理切片是在不削弱审计与 fail-closed 语义的前提下，定义**何时、由谁、以何种幂等方式**清理已不再需要的检查点，并避免磁盘无限增长。

本建议稿只讨论本地检查点目录；不包含多实例共享盘调度、远程 Git、生产默认启用或链式 Rollback。

## 2. 现状与不变量（不得破坏）

| 项 | 边界 |
| --- | --- |
| 检查点创建 | Apply 写盘前原子发布；同一 `applyRunId` 禁止覆盖 |
| 产品 Rollback | 成功 Rollback **不**删除检查点；DB `apply/succeeded` 为资格权威 |
| 成功 Rollback 唯一性 | 051 约束同一 `SourceApplyRunId` 至多一条 `rollback/succeeded` |
| 生成器删除边界 | 物理清理必须独立、显式授权、可审计；禁止静默 unlink（见 `development-quality.md` R-20260730） |

因此：清理不是“回滚流程的副作用”，必须是**独立、可配置、可观测**的保留策略。

## 3. 建议的首切片范围

### 纳入

1. **资格（推荐）**：仅当 DB 同时满足以下全部条件时，才允许删除对应 `applyRunId` 检查点目录：
   - `fn_codegeneration_run` 存在 `operationKind='apply' AND status='succeeded'` 且 `Id = applyRunId`；
   - 存在 `operationKind='rollback' AND status='succeeded' AND SourceApplyRunId = applyRunId`（已成功产品回滚）；
   - 可选：检查点 `AppliedManifest` 与当前工作区 Manifest 仍一致，或工作区已回到 `PreviousManifest` 语义（避免“已回滚但磁盘又被改写”时误删唯一证据）。
2. **执行面**：Worker 或 Migrator 后台扫描（**非** HTTP 公共 API 首切片），受 `CodeGeneration:Apply` 同一 `WorkspaceRoot` 与 opt-in 开关约束。
3. **策略**：配置化 `RetentionDays` 或 `MaxCheckpointCount`（二选一或组合，Spec 前确认）；未达策略前只记账不删除。
4. **幂等**：删除前复验目录结构与 `checkpoint.json` 摘要；删除失败保留并下轮重试；`affected` 语义为“目录存在且已删除”。
5. **观测**：结构化日志与计数（已扫描/已删除/已跳过/失败），不得记录绝对路径或源码正文。
6. **测试**：Unit 覆盖资格矩阵；Integration 使用临时工作区 + 伪造 DB 资格（或测试替身）验证零误删。

### 明确排除

- Rollback 成功时自动删除检查点（与首切片产品语义冲突，除非用户显式改 Spec）
- 按客户端请求删除任意 `applyRunId`
- 多实例分布式锁/队列（留给后续）
- 加密备份、异地复制、容量配额告警
- 清理失败 Apply 的 running 检查点（需独立事故处理 Spec）
- Vue/Layui 管理界面（可列后续）

## 4. 未决问题（Spec 前需确认）

1. 成功 Rollback 后是否**立即**允许清理，还是必须等待 `RetentionDays` 冷却期（推荐冷却期，便于事故复核）。
2. 从未发起 Rollback 的 succeeded Apply 检查点是否永久保留，直到显式运维策略（推荐默认保留，仅“已 succeeded Rollback”可清理）。
3. 清理执行宿主：仅 Worker，还是 Api 进程内 HostedService（推荐 Worker，与 Apply Gate 解耦）。
4. 是否需要将“已清理”写入 DB 摘要列或独立审计表（推荐首切片仅日志 + 可选 Host 操作日志，不扩表）。

## 5. 验收与文档流转

1. 用户确认本建议稿 → 带日期 Spec（Approved）→ 实施 Plan → RED 先行。
2. 双库/双端非本切片门禁；不得把“能删目录”标为产品 Rollback `Verified`。
3. Verification 须区分“产品 Rollback 已交付”与“检查点保留清理已交付”。

## 6. 规则/Skill

未触发规则或 Skill 升级条件；本文件仅为评估建议稿。
