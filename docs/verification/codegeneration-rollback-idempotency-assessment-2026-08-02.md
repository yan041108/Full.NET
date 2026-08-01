# CodeGeneration 重复/失败重试 Rollback 幂等评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `c6b7942`
- 状态：**已关闭** → Spec [2026-08-02-codegeneration-rollback-idempotency-design.md](../superpowers/specs/2026-08-02-codegeneration-rollback-idempotency-design.md)（Approved）
- 上游证据：[产品 Rollback 验证](codegeneration-product-rollback-2026-08-02.md)、[多实例互斥验证](codegeneration-distributed-workspace-gate-2026-08-02.md)

## 1. 结论

首切片对已成功 Rollback 的 Apply 返回 `rollback_already_applied`（409）。客户端重试、网关超时重放与自动化编排需要 **幂等成功回放**；`failed` 回滚行应允许 **安全重试**。本切片不引入链式多 Apply 单次 API、不删检查点、不改 HTTP 路径与双端 UI。

## 2. 建议范围

### 纳入

1. 已存在 `rollback/succeeded` 且工作区仍处回滚后目标 Manifest：返回 `200` 与首次成功相同 `runId`/`applyRunId`/`artifactCount`/`manifestSha256`；`changedArtifactCount` 为 `0`（无写盘）。
2. 已存在 `rollback/succeeded` 但工作区已偏离回滚后目标：返回 `rollback_conflict`，零写入。
3. 仅存在 `rollback/failed`：允许新一次完整回滚（新 `runId`）。
4. 存在 `rollback/running`：映射 `rollback_busy`。
5. Unit + Integration 更新；不新增迁移。

### 排除

链式多 Apply 编排、远程 Git、生产默认启用、检查点删除、`MaxCheckpointCount`、Vue/Layui 变更。

## 3. 规则/Skill

未触发规则或 Skill 升级条件；本文件仅为评估建议稿。