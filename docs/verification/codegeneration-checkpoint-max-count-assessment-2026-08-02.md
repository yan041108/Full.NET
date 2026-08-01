# CodeGeneration 检查点容量配额（MaxCheckpointCount）评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `8362c28`
- 状态：**已关闭** → Spec [2026-08-02-codegeneration-checkpoint-max-count-design.md](../superpowers/specs/2026-08-02-codegeneration-checkpoint-max-count-design.md)（Approved）
- 上游：[检查点保留清理验证](codegeneration-checkpoint-retention-2026-08-02.md)

## 1. 结论

`RetentionDays` 已交付；当 Apply 频繁而回滚较少时，磁盘仍可能堆积未过冷却期的检查点。下一切片在 **同一 Worker Runner** 上增加 `MaxCheckpointCount`（0=禁用）：磁盘目录数超限时，在既有资格（成功 Rollback + Manifest 安全）下按最旧 Rollback 优先删除，可忽略冷却期，仍受 `MaxDeletesPerRun` 约束。

## 2. 排除

远程 Git、生产默认启用、链式回滚、HTTP/双端、新迁移。

## 3. 规则/Skill

未触发规则或 Skill 升级条件。