# CodeGeneration 检查点保留清理验证记录

- 日期：2026-08-02
- 代码基线：`main` @ 待提交
- 状态：**Build-verified**
- Spec：[2026-08-02-codegeneration-checkpoint-retention-design.md](../superpowers/specs/2026-08-02-codegeneration-checkpoint-retention-design.md)
- 任务快照：`codegeneration-checkpoint-retention-20260802`

## 交付范围

Worker 后台按 DB 资格与 `RetentionDays` 冷却期清理已成功产品回滚的本地检查点目录；默认关闭；不扩 HTTP/双端/迁移。

## 验证

| 项 | 结果 |
| --- | --- |
| `TryDeleteAsync` + Runner 单元测试 | 5/5 |
| CodeGeneration affected inner（双 Provider） | 32/32 |

## 规则/Skill

未触发规则或 Skill 升级条件。