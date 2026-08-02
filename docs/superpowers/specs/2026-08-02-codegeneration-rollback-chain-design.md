# CodeGeneration 链式多 Apply 回滚设计

**状态：** Approved for implementation
**日期：** 2026-08-02
**基线：** `main` @ `5a4c552`

## 1. HTTP

`POST /api/v1/code-generation/runs/rollback-chain`；请求 `{ "applyRunIds": ["<uuid>", ...] }`；响应 `{ "rollbacks": [ ... ] }`（元素同单次 Rollback 摘要）。

## 2. 校验

- 长度 2..`MaxRollbackChainLength`（默认 16）；无重复；全部 `apply/succeeded` 且同 `moduleKey+entityKey`。
- 顺序必须等于该实体待回滚栈（`StartedAtUtc DESC, Id`）的前缀。
- 空栈/单元素/乱序/跨实体 → `codegen.run.invalid_rollback_chain`。

## 3. 编排

整链一次 Gate；Git `Synchronize` 一次；每步复用单 Apply 回滚内核（含幂等重放、逐步 `Publish`）；任一步失败即返回该步错误，已成功步骤保持 committed。

## 4. 排除

双端 UI、新迁移、生产默认启用。