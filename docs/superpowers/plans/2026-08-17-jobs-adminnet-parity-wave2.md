# Jobs Admin.NET 对标波次 2 实施计划

> **For agentic workers:** Use `fullnet-module-delivery`. RED first.

**Goal：** 切片 1 按任务集群串行；切片 2–4 补齐执行历史、Cron 解释、只读健康。

**Spec：** [`2026-08-17-jobs-per-definition-overlap-control-design.md`](../specs/2026-08-17-jobs-per-definition-overlap-control-design.md)

**快照：**

- `jobs-overlap-control-20260817`
- `jobs-execution-history-20260817`
- `jobs-cron-explain-20260817`
- `jobs-health-readonly-20260817`

### Task 1: 重叠控制（096）

- [ ] RED：多 Worker 同定义双 pending 重叠
- [ ] 096 迁移 + JobSql Acquire + Dispatcher skip
- [ ] API/contracts/Vue + inner/slice GREEN

### Task 2: 执行历史

- [ ] 列表过滤 + GET by id + HostJobExecutionsView

### Task 3: Cron 解释

- [ ] humanDescription + nextOccurrencesUtc + 宏预设 + UI

### Task 4: 只读健康

- [ ] 097 heartbeat 表 + host-health API + Vue + jobs.health.read

### Task 5: Closeout

- [ ] verification ×4、路线图、test-matrix、权限库存
