# CodeGeneration 链式多 Apply 回滚评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `4863c6b`
- 状态：**已关闭** → Spec [design](../superpowers/specs/2026-08-02-codegeneration-rollback-chain-design.md)（Approved）
- 上游：[产品 Rollback 验证](codegeneration-product-rollback-2026-08-02.md)、[幂等验证](codegeneration-rollback-idempotency-2026-08-02.md)

## 1. 结论

同一实体多次成功 Apply 后，工作区仅等于**最新**一次 Apply 的 Manifest；对较早 Apply 单独回滚必然因 Manifest 漂移得到 `rollback_conflict`。运维若要回到更早状态，必须按 **LIFO** 依次回滚较新的 Apply。当前单次 `POST .../rollback` 已支持逐步链式调用，但缺少**单次 Gate 内原子编排**与**顺序校验**，自动化与双端「回滚到指定代次」体验不足。

建议下一切片交付 **rollback-chain** 编排：请求携带按新→旧排序的 `applyRunIds`（2..N，上限可配置），在共享 `CodeGenerationApplyGate` 内顺序调用既有单 Apply 回滚逻辑；任一步失败则停止并返回该步错误，已成功步骤保持 committed（不跨运行事务）。

## 2. 建议纳入

1. **HTTP**：`POST /api/v1/code-generation/runs/rollback-chain`；请求 `{ "applyRunIds": ["<uuid>", ...] }`（禁止额外成员）；响应 `{ "rollbacks": [ { runId, applyRunId, artifactCount, changedArtifactCount, manifestSha256 }, ... ] }`（摘要化，与单次一致）。
2. **校验**：全部 `apply/succeeded`；同一 `moduleKey+entityKey`；尚未 `rollback/succeeded`；列表必须严格按 `StartedAtUtc DESC`（同秒以 `Id` 打破平局）与当前工作区可 LIFO 回滚；空列表或单元素映射为校验错误。
3. **编排**：复用 `CodeGenerationRollbackService.RollbackAsync` 内核；整链一次 `TryEnter`；幂等重放仍按单 Apply 规则。
4. **权限**：复用 `codegen.runs.rollback`；OpenAPI + client-contracts；Vue/Layui 可选「回滚到此次 Apply」展开为链（若 UI 纳入须双端同步）。
5. **测试**：Unit（顺序非法、中途冲突、全成功）；Integration 双库；真实栈 E2E 可后置到 UI 切片。

## 3. 明确排除

- 跨实体/跨模块批量
- 删除检查点策略变更（沿用单步 opt-in）
- 生产默认启用、远程 Git 语义变更
- 新迁移（除非发现必须持久化链 Id）

## 4. 未决问题（Spec 前）

1. 链长度上限（建议默认 16，可配置）。
2. 双端首切片是否仅 API + contracts，UI 跟随后续小切片。
3. 中途失败后客户端如何安全重试（从失败 `applyRunId` 起截断重放）。

## 5. 规则/Skill

未触发规则或 Skill 升级条件。