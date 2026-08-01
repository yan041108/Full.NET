# CodeGeneration Rollback 重复请求幂等与失败重试设计

**状态：** Approved for implementation
**日期：** 2026-08-02
**基线：** `main` @ `c6b7942`

## 1. 决策摘要

对同一 `applyRunId` 的重复 Rollback POST：若 DB 已有 `rollback/succeeded` 且磁盘 Manifest 仍等于检查点 `PreviousManifest`（或规范空 Manifest），返回 `200` 与既有成功摘要一致，`changedArtifactCount=0`，不写盘、不占 Gate 写路径。

若已成功但磁盘已偏离回滚后目标，返回 `rollback_conflict`。若仅有 `failed` 行，允许重试。若存在 `running`，返回 `rollback_busy`。

## 2. HTTP

契约不变；`rollback_already_applied` 本切片起不再用于重复成功场景（保留常量供兼容文档，Integration 改为期望 `200`）。

## 3. 排除

链式回滚、迁移、双端、检查点清理策略变更。