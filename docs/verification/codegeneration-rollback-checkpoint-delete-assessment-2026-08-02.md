# CodeGeneration 回滚后删除检查点评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `dfd876f`
- 状态：**已关闭** → Spec [2026-08-02-codegeneration-rollback-checkpoint-delete-design.md](../superpowers/specs/2026-08-02-codegeneration-rollback-checkpoint-delete-design.md)（Approved）
- 上游：[检查点保留清理](codegeneration-checkpoint-retention-2026-08-02.md)、[产品 Rollback](codegeneration-product-rollback-2026-08-02.md)

## 1. 结论

Worker 保留清理面向冷却期与容量；部分部署希望在 **产品 Rollback 成功收敛后** 立即释放对应 `applyRunId` 检查点目录。下一切片以 opt-in 配置在 API 进程 Rollback 成功后调用既有 `TryDeleteAsync`，失败只记日志、不改变 succeeded 终态。

## 2. 排除

链式多 Apply、生产默认启用、HTTP/双端、Worker 策略变更。

## 3. 规则/Skill

未触发规则或 Skill 升级条件。